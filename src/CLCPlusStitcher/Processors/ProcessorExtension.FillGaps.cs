// <copyright file="ProcessorExtension.FillGaps.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher.Processors
{
	using System;
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
		public static IProcessor<Polygon> FillGaps(this IProcessor<Polygon> container, ICollection<Polygon> toMerge,
			ICollection<Polygon>? overlaps, ILogger<Processor>? logger = null)
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}

			return container.Chain("FillGaps", geometries => ProcessorExtension.FillGaps(geometries, toMerge, overlaps, progress =>
				{
					logger?.LogDebug("{ProcessorName} [{DataName}] progress: {Progress:P}", "FillGaps", container.DataName, progress);
				})
				.ToList());
		}

		public static IEnumerable<Polygon> FillGaps(ICollection<Polygon> geometries, ICollection<Polygon> toMerge,
			ICollection<Polygon>? overlaps, Action<double>? onProgress = null)
		{
			List<Polygon> polygons = geometries.ToList();
			ISpatialIndex<Polygon> spatialIndexEnvelopes = new STRtree<Polygon>();

			foreach (Polygon polygon in overlaps ?? geometries)
			{
				spatialIndexEnvelopes.Insert(polygon.EnvelopeInternal, polygon);
			}

			int counter = 0;

			Parallel.ForEach(toMerge, other =>
			{
				if (!spatialIndexEnvelopes.Query(other.EnvelopeInternal)
					.Any(x => x.Relate(other)[Location.Interior, Location.Interior] == Dimension.Surface))
				{
					polygons.Add(other);
				}

				int progress = Interlocked.Increment(ref counter);

				int previousProgress = (progress - 1) / (toMerge.Count / 9);
				int currentProgress = progress / (toMerge.Count / 9);

				if (previousProgress < currentProgress)
				{
					onProgress?.Invoke(currentProgress / 10.0);
				}
			});

			onProgress?.Invoke(1);
			return polygons;
		}
	}
}