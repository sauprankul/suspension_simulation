// Simple solid bumper for exercising the coarse STEP/FEA workflow. Dimensions in mm.
SetFactory("OpenCASCADE");
Cylinder(1) = {0, 0, 0, 0, 0, 40, 10, 2*Pi};
