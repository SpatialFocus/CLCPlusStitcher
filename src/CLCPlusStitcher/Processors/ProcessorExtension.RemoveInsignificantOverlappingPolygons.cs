// <copyright file="ProcessorExtension.RemoveInsignificantOverlappingPolygons.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher.Processors
{
	using System;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;
	using System.Threading.Tasks;
	using ClcPlusRetransformer.Core.Processors;
	using NetTopologySuite.Geometries;

	public static partial class ProcessorExtension
	{
		public static IProcessor<Polygon> RemoveInsignificantOverlappingPolygons(this IProcessor<Polygon> container, Geometry pu1,
			Geometry pu2, double areaThreshold)
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}

			return container.Chain("RemoveInsignificantOverlappingPolygons",
				geometries => ProcessorExtension.RemoveInsignificantOverlappingPolygons(geometries, pu1, pu2, areaThreshold).ToList());
		}

		public static IEnumerable<Polygon> RemoveInsignificantOverlappingPolygons(ICollection<Polygon> geometries, Geometry pu1,
			Geometry pu2, double areaThreshold)
		{
			ConcurrentBag<Geometry> geometriesProcessed = new();

			Parallel.ForEach(geometries, geometry =>
			{
				if (geometry.Intersects(pu1) && geometry.Intersects(pu2))
				{
					if (geometry.Intersection(pu1).Area >= areaThreshold && geometry.Intersection(pu2).Area >= areaThreshold)
					{
						geometriesProcessed.Add(geometry);
					}
				}
				else
				{
					geometriesProcessed.Add(geometry);
				}
			});

			return geometriesProcessed.ToArray().FlattenAndIgnore<Polygon>();
		}
	}
}