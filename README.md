# EIDA
Automated IDA-to-fragility pipeline for parametric seismic fragility generation of RC moment-frame buildings with SSI in ETABS + MATLAB

EIDA (Extended Incremental Dynamic Analysis) is a fault-tolerant, 
high-throughput pipeline for automated seismic fragility dataset 
generation of reinforced concrete moment-frame buildings.

The framework integrates CSI ETABS (via COM API), a C# orchestration 
engine, and MATLAB into a unified workflow. It accepts any user-defined 
RC moment-frame model as a base ETABS .edb file and performs a full 
Monte Carlo parametric sweep across structural geometry, material 
properties, and soil conditions — without any hard-coded parameters 
or manual intervention.

Key features:
- Automated model generation via stochastic parametric sampling
- Soil-structure interaction (SSI) using per-archetype Gazetas spring 
  recalculation for three ASCE 7 site classes
- Dual-algorithm nonlinear pushover damage-state identification 
  (component-based + global stiffness-degradation)
- Direct text injection of nonlinear hinge definitions, bypassing 
  ETABS API restrictions for bulk assignment
- Hunt-and-Fill IDA scaling loop with real-time Cornell power-law 
  PSDM fitting and three-criterion collapse detection
- Lognormal fragility curve extraction (DS1–DS5) per archetype
- GLME meta-regression engine producing closed-form, spreadsheet-
  evaluable fragility equations
- Crash recovery via plain-text state file — safe to interrupt and 
  resume at any point

Developed as part of a parametric seismic fragility study of 
gravity-load-designed RC grid-like frame structures in low-to-moderate 
seismicity regions (Pakistan, Malaysia).

Companion article: [link to Structures paper once published]
MethodsX article: [link once published]
