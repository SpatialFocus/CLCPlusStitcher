// <copyright file="ProcessorExtension.FillGaps.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher.Processors
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;
	using ClcPlusRetransformer.Core.Processors;
	using Microsoft.Extensions.Logging;
	using NetTopologySuite.Geometries;
	using NetTopologySuite.Index;
	using NetTopologySuite.Index.Strtree;

	public static partial class ProcessorExtension
	{
		public static IProcessor<Polygon> FillGaps(this IProcessor<Polygon> container, ICollection<Polygon> polygonsToMerge,
			ICollection<Polygon> polygonAreasToExclude, ILogger<Processor>? logger = null)
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}

			return container.Chain("FillGaps", geometries => ProcessorExtension.FillGaps(geometries, polygonsToMerge, polygonAreasToExclude,
					progress =>
					{
						logger?.LogDebug("{ProcessorName} [{DataName}] progress: {Progress:P}", "FillGaps", container.DataName, progress);
					})
				.ToList());
		}

		public static IEnumerable<Polygon> FillGaps(ICollection<Polygon> geometries, ICollection<Polygon> polygonsToMerge,
			ICollection<Polygon> polygonAreasToExclude, Action<double>? onProgress = null)
		{
			ConcurrentBag<Polygon> polygons = new(geometries.ToList());
			ISpatialIndex<Polygon> spatialIndexEnvelopes = new STRtree<Polygon>();

			foreach (Polygon polygon in polygonAreasToExclude)
			{
				spatialIndexEnvelopes.Insert(polygon.EnvelopeInternal, polygon);
			}

			int counter = 0;

			Parallel.ForEach(polygonsToMerge, mergePolygon =>
			{
				if (spatialIndexEnvelopes.Query(mergePolygon.EnvelopeInternal)
					.All(x => x.Relate(mergePolygon)[Location.Interior, Location.Interior] != Dimension.Surface ||
						x.Intersection(mergePolygon).Area < 0.01))
				{
					polygons.Add(mergePolygon);
				}
				////else
				////{
				////	IEnumerable<Polygon> enumerable = spatialIndexEnvelopes.Query(mergePolygon.EnvelopeInternal)
				////		.Where(x => x.Relate(mergePolygon)[Location.Interior, Location.Interior] == Dimension.Surface);
				////	if (enumerable.All(x => x.Intersection(mergePolygon).Area < 0.01))
				////	{
				////		polygons.Add(mergePolygon);
				////	}
				////}

				int progress = Interlocked.Increment(ref counter);

				int previousProgress = (progress - 1) / (polygonsToMerge.Count / 9);
				int currentProgress = progress / (polygonsToMerge.Count / 9);

				if (previousProgress < currentProgress)
				{
					onProgress?.Invoke(currentProgress / 10.0);
				}
			});

			onProgress?.Invoke(1);
			return polygons.ToList();
		}
	}
}