// <copyright file="Program.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Threading.Tasks;
	using ClcPlusRetransformer.Core;
	using ClcPlusRetransformer.Core.Processors;
	using ClcPlusRetransformer.Core.Processors.Extension;
	using CLCPlusStitcher.Processors;
	using Microsoft.Extensions.Configuration;
	using Microsoft.Extensions.DependencyInjection;
	using Microsoft.Extensions.Logging;
	using NetTopologySuite.Geometries;
	using Serilog;

	public class Program
	{
		public static async Task Main()
		{
			IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");

			IConfigurationRoot config = builder.Build();

			ServiceCollection serviceCollection = new();
			serviceCollection.AddLogging(loggingBuilder =>
				loggingBuilder.AddSerilog(new LoggerConfiguration().ReadFrom.Configuration(config.GetSection("Logging")).CreateLogger()));

			serviceCollection.AddTransient(typeof(Processor<>));
			serviceCollection.AddTransient(typeof(ChainedProcessor<,>));
			serviceCollection.AddTransient<ProcessorFactory>();

			ServiceProvider provider = serviceCollection.BuildServiceProvider();

			ILogger<Program> logger = provider.GetRequiredService<ILogger<Program>>();

			logger.LogInformation("Workflow started");
			Stopwatch stopwatch = Stopwatch.StartNew();

			PrecisionModel precisionModel = new(int.Parse(config["Precision"]));

			// Load features and intersect with individual AOIs
			IConfigurationSection pu1Section = config.GetSection("PU1");
			Input pu1Input = pu1Section.GetSection("Input").Get<Input>();

			MultiPolygon pu1Aoi = new(provider.LoadFromFile<Polygon>(pu1Section.GetSection("AOI").Get<Input>(), precisionModel)
				.Buffer(0)
				.Execute()
				.ToArray());
			IProcessor<Polygon> pu1Processor = provider.LoadFromFile<Polygon>(pu1Input, precisionModel,
					provider.GetRequiredService<ILogger<Processor>>())
				.Buffer(0)
				.Intersect(pu1Aoi);

			IConfigurationSection pu2Section = config.GetSection("PU2");
			Input pu2Input = pu2Section.GetSection("Input").Get<Input>();

			MultiPolygon pu2Aoi = new(provider.LoadFromFile<Polygon>(pu2Section.GetSection("AOI").Get<Input>(), precisionModel)
				.Buffer(0)
				.Execute()
				.ToArray());
			IProcessor<Polygon> pu2Processor = provider.LoadFromFile<Polygon>(pu2Input, precisionModel,
					provider.GetRequiredService<ILogger<Processor>>())
				.Buffer(0)
				.Intersect(pu2Aoi);

			Task<ICollection<Polygon>>? task5 = Task.Run(() => pu1Processor.Execute());
			Task<ICollection<Polygon>>? task6 = Task.Run(() => pu2Processor.Execute());

			await Task.WhenAll(task5, task6);

			ICollection<Polygon> pu1Intersects = task5.Result;
			ICollection<Polygon> pu2Intersects = task6.Result;

			if (bool.Parse(config["CompareTopologicalEquality"]))
			{
				Task<ICollection<Polygon>>? task1 = Task.Run(() => pu1Processor.Overlap(pu1Aoi).Execute());
				Task<ICollection<Polygon>>? task2 = Task.Run(() => pu2Processor.Overlap(pu2Aoi).Execute());

				await Task.WhenAll(task1, task2);

				ICollection<Polygon> pu1BorderPolygons = task1.Result;
				ICollection<Polygon> pu2BorderPolygons = task2.Result;

				int i = 0;

				foreach (var pu1BorderPolygon in pu1BorderPolygons)
				{
					Polygon? polygon = pu2BorderPolygons.FirstOrDefault(x => x.EqualsTopologically(pu1BorderPolygon));

					if (polygon != null)
					{
						pu2Intersects.Remove(polygon);
						i++;
					}
				}

				logger.LogInformation($"Stitching: {i} duplicated polygons removed from PU2");
			}
			else
			{
				IProcessor<Polygon> pu1OverlapsAoi = pu1Processor.Overlap(pu1Aoi);
				IProcessor<Polygon> pu2OverlapsAoi = pu2Processor.Overlap(pu2Aoi);
				List<Polygon> pu1Polygons = pu1OverlapsAoi.Execute().ToList();
				List<Polygon> pu2Polygons = pu2OverlapsAoi.Execute().ToList();
				List<Polygon> pu1PolygonsResults = new();
				List<Polygon> pu1PolygonsRemaining = new();

				foreach (Polygon pu1Polygon in pu1Polygons)
				{
					List<Polygon> polygons = pu2Polygons
						.Where(x => x.Relate(pu1Polygon)[Location.Interior, Location.Interior] == Dimension.Surface)
						.ToList();

					if (polygons.Any())
					{
						Geometry geometry = pu1Polygon.Intersection(pu1Aoi);
						Geometry multiPolygonPu1 = new MultiPolygon(geometry.FlattenAndIgnore<Polygon>().Where(x => x.Area > 1).ToArray());
						//Polygon result = (Polygon)geometry;

						foreach (Polygon polygon in polygons)
						{
							Geometry intersection = polygon.Intersection(pu2Aoi);

							MultiPolygon multiPolygon = new(intersection.FlattenAndIgnore<Polygon>().Where(x => x.Area > 1).ToArray());

							multiPolygonPu1 = multiPolygonPu1.Union(multiPolygon);

							//result = (Polygon)union;
							pu2Polygons.Remove(polygon);
						}

						if (multiPolygonPu1 is Polygon)
						{
							pu1PolygonsResults.Add((Polygon)multiPolygonPu1);
						}
						else if (multiPolygonPu1 is MultiPolygon && multiPolygonPu1.FlattenAndIgnore<Polygon>().Count() == 1)
						{
							pu1PolygonsResults.Add(multiPolygonPu1.FlattenAndIgnore<Polygon>().First());
						}
						else
						{
							File.WriteAllText(@$"C:\temp\multi_{DateTime.Now.Ticks}.txt", multiPolygonPu1.AsText());

							pu1PolygonsResults.AddRange(multiPolygonPu1.FlattenAndIgnore<Polygon>().Where(x => x.Area > 1));

							//multiPolygonPu1.Save(@"c:\temp\multipolygon.gpkg");
							//throw new InvalidOperationException();
						}

						//pu1PolygonsResults.Add((Polygon)multiPolygonPu1);
					}
					else
					{
						//Polygon? result = pu1PolygonsResults.FirstOrDefault(x =>
						//	x.Relate(pu1Polygon)[Location.Interior, Location.Interior] == Dimension.Surface);

						//if (result != null)
						//{
						//	Geometry intersection = pu1Polygon.Intersection(pu2Aoi);

						//	result = (Polygon)result.Union(intersection);
						//	pu1PolygonsResults.Add((Polygon)result.Union(pu1Polygon));

						//	pu1PolygonsResults.Remove(result);
						//}
						//else
						//{
						pu1PolygonsRemaining.Add(pu1Polygon);

						//}
					}
				}

				foreach (Polygon polygon in pu1OverlapsAoi.Execute())
				{
					pu1Intersects.Remove(polygon);
				}

				foreach (Polygon polygon in pu2OverlapsAoi.Execute())
				{
					pu2Intersects.Remove(polygon);
				}

				foreach (Polygon polygon in pu1PolygonsResults)
				{
					pu1Intersects.Add(polygon);
				}

				foreach (Polygon polygon in pu1PolygonsRemaining)
				{
					pu1Intersects.Add(polygon);
				}

				foreach (Polygon polygon in pu2Polygons)
				{
					pu2Intersects.Add(polygon);
				}

				logger.LogInformation($"Stitching: {pu1PolygonsResults.Count} polygons merged into PU1");
				logger.LogInformation($"Stitching: {pu2OverlapsAoi.Execute().Count - pu2Polygons.Count} polygons removed from PU2");
			}

			// Export output PU1 and PU2
			Input pu1Output = pu1Section.GetSection("Output").Get<Input>();
			Input pu2Output = pu2Section.GetSection("Output").Get<Input>();
			Task? task3 = Task.Run(() => pu1Intersects.Save(pu1Output.FileName, precisionModel, layerName: pu1Output.LayerName));
			Task? task4 = Task.Run(() => pu2Intersects.Save(pu2Output.FileName, precisionModel, layerName: pu2Output.LayerName));

			await Task.WhenAll(task3, task4);

			logger.LogInformation("Saved PU outputs");

			stopwatch.Stop();
			logger.LogInformation("Workflow finished in {Time}ms", stopwatch.ElapsedMilliseconds);

			if (bool.Parse(config["WaitForUserInputAfterCompletion"]))
			{
				Console.WriteLine("=== PRESS A KEY TO PROCEED ===");
				Console.ReadKey();
			}
		}
	}
}