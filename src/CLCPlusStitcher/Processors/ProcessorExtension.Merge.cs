// <copyright file="ProcessorExtension.Merge.cs" company="Spatial Focus GmbH">
// Copyright (c) Spatial Focus GmbH. All rights reserved.
// </copyright>

namespace CLCPlusStitcher.Processors
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using ClcPlusRetransformer.Core.Processors;
	using NetTopologySuite.Geometries;

	public static partial class ProcessorExtension
	{
		public static IProcessor<Polygon> Merge(this IProcessor<Polygon> container, ICollection<Polygon> others)
		{
			if (container == null)
			{
				throw new ArgumentNullException(nameof(container));
			}

			return container.Chain("Merge", geometries => ProcessorExtension.Merge(geometries, others).ToList());
		}

		public static IEnumerable<Polygon> Merge(ICollection<Polygon> geometries, ICollection<Polygon> others)
		{
			List<Polygon> polygons = geometries.ToList();
			polygons.AddRange(others);

			return polygons.Select(x => x.Copy()).Cast<Polygon>();
		}
	}
}