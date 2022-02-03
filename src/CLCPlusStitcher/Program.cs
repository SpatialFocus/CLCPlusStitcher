// <copyright file="Program.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
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

			ICollection<Polygon> pu1Polygons, pu2Polygons;

			if (bool.Parse(config["CompareTopologicalEquality"]))
			{
				Task<ICollection<Polygon>> task11 = Task.Run(() => pu1Processor.Execute());
				Task<ICollection<Polygon>> task12 = Task.Run(() => pu2Processor.Execute());

				await Task.WhenAll(task11, task12);

				pu1Polygons = task11.Result;
				pu2Polygons = task12.Result;

				Task<ICollection<Polygon>> task21 = Task.Run(() => pu1Processor.Overlap(pu1Aoi).Execute());
				Task<ICollection<Polygon>> task22 = Task.Run(() => pu2Processor.Overlap(pu2Aoi).Execute());

				await Task.WhenAll(task21, task22);

				ICollection<Polygon> pu1BorderPolygons = task21.Result;
				ICollection<Polygon> pu2BorderPolygons = task22.Result;

				int i = 0;

				foreach (Polygon pu1BorderPolygon in pu1BorderPolygons)
				{
					Polygon? polygon = pu2BorderPolygons.FirstOrDefault(x => x.EqualsTopologically(pu1BorderPolygon));

					if (polygon != null)
					{
						pu2Polygons.Remove(polygon);
						i++;
					}
				}

				logger.LogInformation($"Stitching: {i} duplicated polygons removed from PU2");
			}
			else
			{
				Task<ICollection<Polygon>> task1 = Task.Run(() => pu1Processor.Execute());
				Task<ICollection<Polygon>> task2 = Task.Run(() => pu2Processor.Execute());

				await Task.WhenAll(task1, task2);

				IProcessor<Polygon> pu1ContainedInAoi = pu1Processor.Contains(pu1Aoi);
				IProcessor<Polygon> pu2ContainedInAoi = pu2Processor.Contains(pu2Aoi);

				IProcessor<Polygon> pu1OverlapsAoi = pu1Processor.Overlap(pu1Aoi);
				IProcessor<Polygon> pu2OverlapsAoi = pu2Processor.Overlap(pu2Aoi);

				IProcessor<LineString> pu1Lines = pu1OverlapsAoi.PolygonsToLines()
					.Clip(pu1Aoi.Buffer(0.0001))
					.Dissolve()
					.CountTooPrecise(precisionModel, provider.GetRequiredService<ILogger<Processor>>());
				IProcessor<LineString> pu2Lines = pu2OverlapsAoi.PolygonsToLines()
					.Clip(pu2Aoi.Buffer(0.0001))
					.Dissolve()
					.CountTooPrecise(precisionModel, provider.GetRequiredService<ILogger<Processor>>());

				Task<ICollection<LineString>> task3 = Task.Run(() => pu1Lines.Execute());
				Task<ICollection<LineString>> task4 = Task.Run(() => pu2Lines.Execute());

				await Task.WhenAll(task3, task4);

				IProcessor<LineString> mergedLines = pu1Lines.SnapTo(pu2Lines.Execute(), precisionModel, 0.001)
					.Merge(pu2Lines.Execute())
					.CountTooPrecise(precisionModel, provider.GetRequiredService<ILogger<Processor>>())
					.Node(precisionModel)
					.Union(provider.GetRequiredService<ILogger<Processor>>())
					.CountTooPrecise(precisionModel, provider.GetRequiredService<ILogger<Processor>>());

				// Construct polygons that are shared between PU1 and PU2
				IProcessor<Polygon> polygonsOverlappingAoi = mergedLines.Polygonize().Overlap(pu1Aoi);

				// Merge them into PU1
				IProcessor<Polygon> pu1Merge = pu1ContainedInAoi.FillGaps(polygonsOverlappingAoi.Execute(), pu1ContainedInAoi.Execute(),
					provider.GetRequiredService<ILogger<Processor>>());

				// Fill the gaps of PU1 with border polygons that have not been shared with PU2
				IProcessor<Polygon> pu1WithFilledGaps = pu1Merge.FillGaps(pu1OverlapsAoi.Execute(), polygonsOverlappingAoi.Execute(),
					provider.GetRequiredService<ILogger<Processor>>());
				pu1Polygons = pu1WithFilledGaps.Execute();

				// Fill the gaps of PU2 with border polygons that have not been shared with PU1 AND are not part of PU1 already
				IProcessor<Polygon> pu2WithFilledGaps = pu2ContainedInAoi.FillGaps(pu2OverlapsAoi.Execute(),
					polygonsOverlappingAoi.Merge(pu1OverlapsAoi.Execute()).Execute(), provider.GetRequiredService<ILogger<Processor>>());
				pu2Polygons = pu2WithFilledGaps.Execute();
			}

			// Export output PU1 and PU2
			Input pu1Output = pu1Section.GetSection("Output").Get<Input>();
			Input pu2Output = pu2Section.GetSection("Output").Get<Input>();
			Task task5 = Task.Run(() =>
				pu1Polygons.Save(pu1Output.FileName, precisionModel, layerName: pu1Output.LayerName, puName: pu1Section["Name"]));
			Task task6 = Task.Run(() =>
				pu2Polygons.Save(pu2Output.FileName, precisionModel, layerName: pu2Output.LayerName, puName: pu2Section["Name"]));

			await Task.WhenAll(task5, task6);

			logger.LogInformation("PU1 [{DataName}] result saved [{ExportFileName}]: {Count} geometries written", pu1Section["Name"],
				pu1Output.FileName, pu1Polygons.Count);
			logger.LogInformation("PU2 [{DataName}] result saved [{ExportFileName}]: {Count} geometries written", pu2Section["Name"],
				pu2Output.FileName, pu2Polygons.Count);

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