# CLCPlusStitcher

Stitch two adjacent CLC+ Backbone PUs together to remove duplicate features.
There are two methods implemented for merging adjacent. 
The simplified method simply compares the two polygons left and right of the PU border for topologically equality.
The advanced method breaks up the polygons left and right of the PU border and merges the two parts together.

## How to create a new release

```
git tag -a v0.1 -m "Release 0.1 with xyz"
git push origin v0.1
```

## Processing workflow

The following steps outline the processing of the Stitching tool in the non-simplified (CompareTopologicalEquality = false) setting:

- Move all the polygons that are completely __contained__ inside the PU directly to the result
- Select the polygons that __overlap__ with the PU border, convert them __polygons to lines__
- __Clip__ the lines and the PU border and __dissolve__
- __Snap__ the PU1 lines to the PU2 lines with 1mm snapping-distance
- __Merge__ the (snapped) PU1 lines and the PU2 lines, then __node__ and __union__ the result
- __Polygonize__ the lines and merge them into the PU1 result (from step 1)
- __Fill the gaps__ of PU1 with border polygons that have not been shared with PU2
- __Fill the gaps__ of PU2 with border polygons that have not been shared with PU1 _AND_ are not part of PU1 already (though the merge earlier)
- __Save__ both the PU1 and PU2 results to file

----

Made with :heart: by [Spatial Focus](https://spatial-focus.net/)