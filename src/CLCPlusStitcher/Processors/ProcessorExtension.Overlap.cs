// <copyright file="ProcessorExtension.Overlap.cs" company="Spatial Focus GmbH">
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
	using NetTopologySuite.Geometries.Prepared;

	public static partial class ProcessorExtension
	{
		public static IProcessor<TGeometryType> Overlap<TGeometryType>(this IProcessor<TGeometryType> container, Geometry otherGeometry)
			where TGeometryType : Geometry
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}

			return container.Chain("Overlap", geometries => ProcessorExtension.Overlap(geometries, otherGeometry).ToList());
		}

		public static IEnumerable<TGeometryType> Overlap<TGeometryType>(ICollection<TGeometryType> geometries, Geometry otherGeometry)
			where TGeometryType : Geometry
		{
			ConcurrentBag<Geometry> geometriesProcessed = new();

			IPreparedGeometry otherGeometryPrepared = new PreparedGeometryFactory().Create(otherGeometry);

			Parallel.ForEach(geometries, geometry =>
			{
				if (otherGeometryPrepared.Overlaps(geometry))
				{
					geometriesProcessed.Add(geometry);
				}
			});

			return geometriesProcessed.ToArray().FlattenAndIgnore<TGeometryType>();
		}
	}
}