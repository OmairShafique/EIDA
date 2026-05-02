using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ETABSv1;
using System.Text.RegularExpressions;
using System.Diagnostics.Eventing.Reader;
using System.Security.Cryptography.X509Certificates;

namespace ETABSPlugInAPI
{
    /// <summary>
    /// Represents a material in the ETABS model.
    /// </summary>
    public class MaterialInfo
    {
        // Basic Info
        public string Name { get; set; }
        public eMatType MaterialType { get; set; }
        public int Color { get; set; }
        public string Notes { get; set; }

        // Mechanical Properties
        public double UnitWeight { get; set; }
        public double[] ElasticModulus { get; set; } // E
        public double[] PoissonsRatio { get; set; } // U
        public double[] SheerModulus { get; set; } // G
        public double[] ThermalCoeff { get; set; }   // A

        // Strength Properties (will only be populated for the relevant material type)
        public double FcPrime { get; set; } // Concrete Compressive Strength
        public double Fy { get; set; }      // Steel Yield Strength
        public double Fu { get; set; }      // Steel Ultimate Strength
        public double EFy { get; set; }
        public double EFu { get; set; }
        public StressStrainType SteelStressStrainType { get; set; }
        public HysteresisType SteelHysteresisType { get; set; }

        // Concrete-specific properties
        public bool IsLightweight { get; set; }
        public double FcsFactor { get; set; }
        public StressStrainType ConcreteStressStrainType { get; set; }
        public HysteresisType ConcreteHysteresisType { get; set; }
        public double StrainAtFc { get; set; }
        public double StrainUltimate { get; set; }
        public double FrictionAngle { get; set; }
        public double DilationAngle { get; set; }

        public double StrainAtHardening { get; set; }
        public double StrainAtMaxStress { get; set; }
        public double StrainAtRupture { get; set; }

        // Concrete Stress-Strain Properties
        public enum StressStrainType
        {
            UserDefined = 0,
            ParametricSimple = 1,
            ParametricMander = 2
        }

        // Concrete Hysteresis Types
        public enum HysteresisType
        {
            Elastic = 0,
            Kinematic = 1,
            Takeda = 2,
            Pivot = 3,
            Concrete = 4,
            BRBHardening = 5,
            Degrading = 6,
            Isotropic = 7
        }


    }

    /// <summary>
    /// Represents a frame property in the ETABS model.
    /// </summary>
    public class FrameSectionInfo
    {
        // Basic Info
        public string Name { get; set; }
        public MaterialInfo Material { get; set; }
        public string Shape { get; set; }

        // Dimensions (various shapes)
        public double Depth { get; set; }             // T3: Total depth (vertical)
        public double Width { get; set; }             // T2: Width (top flange width or rectangle width)
        public double FlangeThickness { get; set; }   // Tf: Top flange thickness
        public double WebThickness { get; set; }      // Tw: Web thickness
        public double BottomFlangeWidth { get; set; } // T2b: Bottom flange width (for I-section)
        public double BottomFlangeThickness { get; set; } // Tfb: Bottom flange thickness

        public double Fy { get; set; } // 

        // Property Modifiers
        public double AreaMod { get; set; }
        public double TorsionMod { get; set; }
        public double I22Mod { get; set; }
        public double I33Mod { get; set; }
        public double Shear22Mod { get; set; }
        public double Shear33Mod { get; set; }
        public double MassMod { get; set; }
        public double WeightMod { get; set; }
    }

    /// <summary>
    /// Represents an area section in the ETABS model.
    /// </summary>
    public class AreaSectionInfo
    {
        // Basic Info
        public string Name { get; set; }
        public string AreaType { get; set; } // Slab, Wall, Deck
        public MaterialInfo MaterialName { get; set; }
        public eSlabType SlabType { get; set; } // Slab, Wall, Deck
        public double Thickness { get; set; }
        public eShellType ShellType { get; set; } // Shell-Thin, Shell-Thick, Membrane

        public eWallPropType WallPropType { get; set; } // Wall Property Type

    }

    /// <summary>
    /// Represents a load pattern in the ETABS model.
    /// </summary>
    public class LoadPatternInfo
    {
        public string Name { get; set; }
        public eLoadPatternType PatternType { get; set; }
        public double SelfWeightMultiplier { get; set; }
        public bool IsAutoLoad { get; set; }
        public string AutoLoadType { get; set; } // e.g., "UBC 97", "ASCE 7-16"
    }

    /// <summary>
    /// Represents a load combination in the ETABS model.
    /// </summary>
    public class LoadCombinationInfo
    {
        public class ComboCase
        {
            public string CaseName { get; set; }
            public eCNameType CaseType { get; set; }
            public double ScaleFactor { get; set; }
        }

        public string Name { get; set; }
        public MyComboType ComboType { get; set; } // Corrected to eComboTypeAll
                                                   // final result is the Envelope of the two inner combos

        public List<ComboCase> Cases { get; set; } = new List<ComboCase>();

        public enum MyComboType
        {
            Linear,
            Nonlinear,
            Envelope,
            Range,
            All
        }
    }

    /// <summary>
    /// Represents a load case in the ETABS model.
    /// </summary>
    public class LoadCasesInfo
    {
        public string Name { get; set; }
        public eLoadCaseType LoadCaseType { get; set; } // Static, Response Spectrum, Time History, etc.



        public int SubTypeInt { get; set; }

        /// <summary>
        /// Gets the descriptive name of the load case subtype (e.g., "Eigen", "Transient").
        /// This property computes the value based on the main 'Type' and 'SubTypeInt'.
        /// </summary>
        public string SubTypeName
        {
            get
            {
                // The logic from our helper function goes directly here.
                switch (this.LoadCaseType)
                {
                    case eLoadCaseType.NonlinearStatic:
                        switch (this.SubTypeInt)
                        {
                            case 1: return "Nonlinear";
                            case 2: return "NonlinearStagedConstruction";
                            default: return "Unknown NonlinearStatic Subtype";
                        }

                    case eLoadCaseType.Modal:
                        switch (this.SubTypeInt)
                        {
                            case 1: return "Eigen";
                            case 2: return "Ritz";
                            default: return "Unknown Modal Subtype";
                        }

                    case eLoadCaseType.LinearHistory:
                        switch (this.SubTypeInt)
                        {
                            case 1: return "Transient";
                            case 2: return "Periodic";
                            default: return "Unknown LinearHistory Subtype";
                        }

                    // You can add other cases here as needed

                    default:
                        return LoadCaseType.ToString(); // Subtype is not applicable for this load case type
                }
            }
        }





        public int numberOfLoads { get; set; } // Number of loads in the load case
        public string[] Loadtype { get; set; }
        public string[] LoadName { get; set; } // Names of the loads in the load case
        public string[] function { get; set; } // Function names for time history or response spectrum cases
        public double[] scaleFactors { get; set; } // Scale factors for the load case
        public double[] timeFactor { get; set; } // Time factor for time history cases
        public double[] arrivalTime { get; set; } // Arrival time for time history cases
        public string[] coordSystem { get; set; } // Coordinate system for the load case
        public double[] angle { get; set; } // Angle for the load case, if applicable

    }

    /// <summary>
    /// Represents a function used in load cases, such as time history or response spectrum functions.
    /// It automatically translates the integer types from the API into descriptive strings.
    /// </summary>
    public class Function
    {
        /// <summary>
        /// The name of the function in ETABS.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Stores the raw integer for the main function type from the API.
        /// </summary>
        public int FuncTypeInt { get; set; }

        /// <summary>
        /// Stores the raw integer for the function subtype from the API.
        /// </summary>
        public int AddTypeInt { get; set; }

        /// <summary>
        /// Gets the descriptive name of the main function type (e.g., "Time History").
        /// </summary>
        public string FuncTypeName
        {
            get
            {
                switch (this.FuncTypeInt)
                {
                    case 1: return "Response Spectrum";
                    case 2: return "Time History";
                    case 3: return "Power Spectral Density";
                    case 4: return "Steady State";
                    case 5: return "Heat Transfer";
                    default: return "Unknown Function Type";
                }
            }
        }

        /// <summary>
        /// Gets the descriptive name of the function subtype (e.g., "From File", "UBC97").
        /// The logic depends on the main function type.
        /// </summary>
        public string AddTypeName
        {
            get
            {
                switch (this.FuncTypeInt)
                {
                    // Case 1: Response Spectrum Functions
                    case 1:
                        switch (this.AddTypeInt)
                        {
                            case 0: return "From file";
                            case 1: return "User";
                            case 2: return "UBC94";
                            case 3: return "UBC97";
                            case 4: return "BOCA96";
                            case 5: return "NBCC95";
                            case 6: return "ASCE7-02";
                            // ... add all other response spectrum codes here ...
                            case 44: return "ASCE7-16";
                            case 45: return "Korean KBC 2016";
                            default: return "Unknown Response Spectrum Subtype";
                        }

                    // Case 2: Time History Functions
                    case 2:
                        switch (this.AddTypeInt)
                        {
                            case 0: return "From file";
                            case 1: return "User";
                            case 2: return "Sine";
                            case 3: return "Cosine";
                            case 4: return "Ramp";
                            case 5: return "Sawtooth";
                            case 6: return "Triangular";
                            case 7: return "User periodic";
                            case 9: return "Matched to Response Spectrum";
                            default: return "Unknown Time History Subtype";
                        }

                    // Case 3: Power Spectral Density Functions
                    case 3:
                        switch (this.AddTypeInt)
                        {
                            case 0: return "From file";
                            case 1: return "User";
                            default: return "Unknown PSD Subtype";
                        }

                    // Case 4: Steady State Functions
                    case 4:
                        switch (this.AddTypeInt)
                        {
                            case 0: return "From file";
                            case 1: return "User";
                            default: return "Unknown Steady State Subtype";
                        }

                    default:
                        return "N/A";
                }
            }
        }


        public double[] TimeValues { get; set; } // Time values for time history functions
        public double[] Value { get; set; } // Values for the function, e.g., acceleration, velocity, displacement

        public double maxValue
        {
            get
            {
                if (Value == null || Value.Length == 0)
                { return 0; }
                // Return the maximum value in the array
                else
                {
                    double max = Math.Abs(Value.Max()); // Initialize max with the first value's absolute value
                    double minn = Math.Abs(Value.Min());
                    // the absolute value of the Value parameter

                    if (max < minn)
                        max = minn; // If the minimum absolute value is greater, use it instead

                    return max;
                }

            }
        }


    }

    /// <summary>
    /// Represents a story in the ETABS model, including all its properties.
    /// </summary>
    public class Story
    {
        /// <summary>
        /// The name of the story.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The elevation of the story in current model units.
        /// </summary>
        public double Elevation { get; set; }

        /// <summary>
        /// The height of the story in current model units.
        /// </summary>
        public double Height { get; set; }


        /// <summary>
        /// The color of the story, used for unique identification.
        /// </summary>
        public int color { get; set; } // Color of the story in ETABS

        /// <summary>
        /// True if the story is a master story.
        /// </summary>
        public bool IsMasterStory { get; set; }

        /// <summary>
        /// The name of the story that this story is similar to. Can be "None".
        /// </summary>
        public string SimilarToStory { get; set; }

        /// <summary>
        /// True if column splicing is active above this story.
        /// </summary>
        public bool SpliceAbove { get; set; }

        /// <summary>
        /// The splice height for columns above this story, if applicable.
        /// </summary>
        public double SpliceHeight { get; set; }
    }

    /// <summary>
    /// Represents a frame object in the ETABS model.
    /// </summary>
    public class FrameObject
    {
        public cSapModel sapModel { get; set; } // Reference to the ETABS model
        public string Name { get; set; }
        public FrameSectionInfo SectionName { get; set; }
        public MaterialInfo MaterialName { get; set; }
        public string Label { get; set; } // Label for the frame object
        public string GUID { get; set; }
        public string Notes { get; set; }
        public string Story { get; set; }
        public string LineType { get; set; }
        public double Length { get; set; }
        // Coordinates of the area vertices
        private List<PointCoordinate> _coordinates = new List<PointCoordinate>();

        /// <summary>
        /// Use to get the coordinates for the Points.
        /// </summary>
        public PointCoordinate[] Points
        {
            /// Gets the coordinates as an array of PointCoordinate objects.
            get => _coordinates.ToArray();
            set
            {
                Points = value;
            }
        }

        /// <summary>
        /// Use to set the coordinates for the Points by providing an array of string[] objects.
        /// </summary>
        public string[] PointNames
        {
            set
            {
                _coordinates.Clear();
                foreach (string pointName in value)
                {
                    double x = 0, y = 0, z = 0;
                    sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z);
                    _coordinates.Add(new PointCoordinate(pointName, x, y, z));
                }
            }
        }


        /// <summary>
        /// Get the story name based on the Z coordinate.
        /// </summary>
        /// <param name="z"></param>
        /// <param name="storyNames"></param>
        /// <param name="storyElevations"></param>
        /// <param name="tolerance"></param>
        /// <returns></returns>
        public static string GetStoryFromZ(double z, string[] storyNames, double[] storyElevations, double tolerance = 0.01)
        {
            for (int i = 0; i < storyElevations.Length; i++)
            {
                if (Math.Abs(z - storyElevations[i]) <= tolerance)
                    return storyNames[i];
            }

            // Fallback: closest story
            double minDiff = double.MaxValue;
            string closestStory = storyNames[0];

            for (int i = 0; i < storyElevations.Length; i++)
            {
                double diff = Math.Abs(z - storyElevations[i]);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closestStory = storyNames[i];
                }
            }

            return closestStory;
        }
    }

    /// <summary>
    /// Represents a joint object in the ETABS model.
    /// </summary>
    public class JointObject
    {
        public string Name { get; set; }

        // Coordinates
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        // 6 DOF Constraint Flags: U1, U2, U3, R1, R2, R3
        public bool[] Constraint { get; set; } = new bool[6];

        public string GUID { get; set; }

        // Individual DOF accessors for clarity
        public bool U1 => Constraint[0];
        public bool U2 => Constraint[1];
        public bool U3 => Constraint[2];
        public bool R1 => Constraint[3];
        public bool R2 => Constraint[4];
        public bool R3 => Constraint[5];

        // Helper properties
        public bool IsFixedTranslation => U1 && U2 && U3;
        public bool IsFixedRotation => R1 && R2 && R3;

        public bool IsPinned => IsFixedTranslation && !R1 && !R2 && !R3;
        public bool IsFullyFixed => IsFixedTranslation && IsFixedRotation;
        public bool IsFree => !U1 && !U2 && !U3 && !R1 && !R2 && !R3;

        // Optional: nice readable label
        public string SupportType
        {
            get
            {
                if (IsFullyFixed) return "Fixed";
                if (IsPinned) return "Pinned";
                if (IsFree) return "Free";
                return "Custom";
            }
        }
    }


    /// <summary>
    /// Represents an area object in the ETABS model.
    /// </summary>
    public class AreaObject
    {
        public cSapModel sapModel { get; set; } // Reference to the ETABS model
        public string Name { get; set; }
        public AreaSectionInfo AreaType { get; set; } // Slab, Wall, Other
        public eShellType ShellType { get; set; }
        public eSlabType? SlabType { get; set; } // Nullable for non-slab
        public eWallPropType WallPropType { get; set; } // Wall Property Type
        public MaterialInfo MaterialName { get; set; }
        public double Thickness { get; set; }
        public string GUID { get; set; }
        public string Notes { get; set; }

        public string StoryName { get; set; } // Story name where the area is located

        // Coordinates of the area vertices
        private List<PointCoordinate> _coordinates = new List<PointCoordinate>();

        /// <summary>
        /// Use to get the coordinates for the Points.
        /// </summary>
        public PointCoordinate[] Points
        {
            /// Gets the coordinates as an array of PointCoordinate objects.
            get => _coordinates.ToArray();
        }

        /// <summary>
        /// Use to set the coordinates for the Points by providing an array of string[] objects.
        /// </summary>
        public string[] PointNames
        {
            set
            {
                _coordinates.Clear();
                foreach (string pointName in value)
                {
                    double x = 0, y = 0, z = 0;
                    sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z);
                    _coordinates.Add(new PointCoordinate(pointName, x, y, z));
                }
            }
        }


        /// <summary>
        /// Efficiently populates the StoryName property for a given list of AreaObjects.
        /// This method iterates through each story in the model once.
        /// </summary>
        /// <param name="sapModel">The active ETABS SapModel object.</param>
        /// <param name="areaObjects">The list of AreaObject instances to update.</param>
        public static void PopulateStoryNames(cSapModel sapModel, List<AreaObject> areaObjects)
        {
            // 1. Create a fast lookup dictionary for all provided area objects.
            Dictionary<string, AreaObject> areaLookup = areaObjects.ToDictionary(a => a.Name);

            // 2. Get all story names from the model.
            int numStories = 0;
            string[] storyNames = null;
            sapModel.Story.GetNameList(ref numStories, ref storyNames);

            if (numStories == 0)
            {
                return;
            }

            // 3. Loop through each story and find the areas on it.
            foreach (string storyName in storyNames)
            {
                int numAreasOnStory = 0;
                string[] areaNamesOnStory = null;
                sapModel.AreaObj.GetNameListOnStory(storyName, ref numAreasOnStory, ref areaNamesOnStory);

                if (numAreasOnStory == 0) continue; // Skip stories with no areas.

                // 4. Update the StoryName for each area found on this story.
                foreach (string areaName in areaNamesOnStory)
                {
                    if (areaLookup.TryGetValue(areaName, out AreaObject areaToUpdate))
                    {
                        areaToUpdate.StoryName = storyName;
                    }
                }
            }

        }

    }

    /// <summary>
    /// Represents a point coordinate in the ETABS model.
    /// </summary>
    public class PointCoordinate
    {
        public string Name { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public PointCoordinate(string name, double x, double y, double z)
        {
            Name = name;
            X = x;
            Y = y;
            Z = z;
        }

        public static Dictionary<string, (double X, double Y, double Z)> GetCoordinatesFromPointNames(cSapModel sapModel, IEnumerable<string> pointNames)
        {
            var coordinates = new Dictionary<string, (double X, double Y, double Z)>();

            foreach (var pointName in pointNames)
            {
                double x = 0, y = 0, z = 0;
                int ret = sapModel.PointObj.GetCoordCartesian(pointName, ref x, ref y, ref z);

                if (ret == 0)
                {
                    coordinates[pointName] = (x, y, z);
                }
                else
                {
                    Console.WriteLine($"[Warning] Could not get coordinates for point: {pointName}");
                }
            }

            return coordinates;
        }
    }



    /// <summary>
    /// Represents a single rectangular bay in a grid system.
    /// </summary>
    public class Bay
    {
        /// <summary>The bottom-left corner X-coordinate.</summary>
        public double X1 { get; set; }
        /// <summary>The bottom-left corner Y-coordinate.</summary>
        public double Y1 { get; set; }
        /// <summary>The top-right corner X-coordinate.</summary>
        public double X2 { get; set; }
        /// <summary>The top-right corner Y-coordinate.</summary>
        public double Y2 { get; set; }
    }

    /// <summary>
    /// Represents a grid system from ETABS, including its lines and bays.
    /// </summary>
    public class Grid
    {
        // --- Public Properties ---
        public string Name { get; private set; }
        public List<Bay> Bays { get; private set; } = new List<Bay>();
        public List<double> XCoordinates { get; set; } = new List<double>();
        public List<double> YCoordinates { get; set; } = new List<double>();


        // --- Calculated Grid Size Properties ---
        public int NumberOfBaysX => XCoordinates.Count > 0 ? XCoordinates.Count - 1 : 0;
        public int NumberOfBaysY => YCoordinates.Count > 0 ? YCoordinates.Count - 1 : 0;

        #region Central Core Calculation Properties

        private int MarginX => NumberOfBaysX / 3;
        private int MarginY => NumberOfBaysY / 3;
        public int CoreSizeX => NumberOfBaysX > 0 ? NumberOfBaysX - 2 * MarginX : 0;
        public int CoreSizeY => NumberOfBaysY > 0 ? NumberOfBaysY - 2 * MarginY : 0;
        public int CentralCoreStartIndexX => MarginX;
        public int CentralCoreEndIndexX => NumberOfBaysX - 1 - MarginX;
        public int CentralCoreStartIndexY => MarginY;
        public int CentralCoreEndIndexY => NumberOfBaysY - 1 - MarginY;

        // --- NEW: Central Core Coordinate Properties ---
        // These return a (X, Y) tuple for each corner of the core.

        /// <summary>Gets the (X, Y) coordinates of the core's bottom-left corner.</summary>
        public (double X, double Y) CentralCoreBottomLeft =>
            NumberOfBaysX > 0 && NumberOfBaysY > 0 ? (XCoordinates[CentralCoreStartIndexX], YCoordinates[CentralCoreStartIndexY]) : (0, 0);

        /// <summary>Gets the (X, Y) coordinates of the core's bottom-right corner.</summary>
        public (double X, double Y) CentralCoreBottomRight =>
            NumberOfBaysX > 0 && NumberOfBaysY > 0 ? (XCoordinates[CentralCoreEndIndexX + 1], YCoordinates[CentralCoreStartIndexY]) : (0, 0);

        /// <summary>Gets the (X, Y) coordinates of the core's top-right corner.</summary>
        public (double X, double Y) CentralCoreTopRight =>
            NumberOfBaysX > 0 && NumberOfBaysY > 0 ? (XCoordinates[CentralCoreEndIndexX + 1], YCoordinates[CentralCoreEndIndexY + 1]) : (0, 0);

        /// <summary>Gets the (X, Y) coordinates of the core's top-left corner.</summary>
        public (double X, double Y) CentralCoreTopLeft =>
            NumberOfBaysX > 0 && NumberOfBaysY > 0 ? (XCoordinates[CentralCoreStartIndexX], YCoordinates[CentralCoreEndIndexY + 1]) : (0, 0);


        #endregion



        private Grid() { }

        /// <summary>
        /// Creates a Grid object using the efficient GetGridSys_2 method.
        /// </summary>
        public static Grid CreateFromETABS(cSapModel sapModel)
        {
            var grid = new Grid();

            // 1. Get the name of the primary grid system (e.g., "G1")
            int numNames = 0;
            string[] gridSystemNames = null;
            sapModel.GridSys.GetNameList(ref numNames, ref gridSystemNames);
            if (numNames == 0) return null;
            grid.Name = gridSystemNames[0];

            // 2. Declare variables for the single API call
            double Xo = 0, Yo = 0, RZ = 0;
            string GridSysType = "";
            int NumXLines = 0, NumYLines = 0;
            string[] GridLineIDX = null, GridLineIDY = null;
            double[] OrdinateX = null, OrdinateY = null;
            bool[] VisibleX = null, VisibleY = null;
            string[] BubbleLocX = null, BubbleLocY = null;

            // 3. Get ALL grid data in a single, efficient API call
            sapModel.GridSys.GetGridSys_2(grid.Name, ref Xo, ref Yo, ref RZ, ref GridSysType,
                                         ref NumXLines, ref NumYLines, ref GridLineIDX, ref GridLineIDY,
                                         ref OrdinateX, ref OrdinateY, ref VisibleX, ref VisibleY,
                                         ref BubbleLocX, ref BubbleLocY);

            // 4. Sort the returned coordinates and store them
            if (OrdinateX != null)
            {
                grid.XCoordinates = OrdinateX.OrderBy(c => c).ToList();
            }
            if (OrdinateY != null)
            {
                grid.YCoordinates = OrdinateY.OrderBy(c => c).ToList();
            }

            // 5. Calculate and create all bay objects from the sorted coordinates
            for (int i = 0; i < grid.NumberOfBaysX; i++)
            {
                for (int j = 0; j < grid.NumberOfBaysY; j++)
                {
                    grid.Bays.Add(new Bay
                    {
                        X1 = grid.XCoordinates[i],
                        Y1 = grid.YCoordinates[j],
                        X2 = grid.XCoordinates[i + 1],
                        Y2 = grid.YCoordinates[j + 1]
                    });
                }
            }

            return grid;
        }
    }

    /// <summary>
    /// Represents a hinge in the ETABS model, which can be used for nonlinear analysis.
    /// </summary>
    public class Hinge
    {
        public string Name { get; set; }


    }

    public class Parsing
    {
        public static List<string> ExtractFrameHingeNames(string filePath)
        {
            var hingeNames = new List<string>();
            bool inSection = false;

            using (var sr = new StreamReader(filePath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (!inSection)
                    {
                        // Look for section start
                        if (line.Equals("$ FRAME HINGE PROPERTIES", StringComparison.OrdinalIgnoreCase))
                        {
                            inSection = true;
                        }
                    }
                    else
                    {
                        // Stop at blank line
                        if (string.IsNullOrWhiteSpace(line))
                            break;

                        // Try to match HINGE "Something"
                        var match = Regex.Match(line, @"HINGE\s+""([^""]+)""");
                        if (match.Success)
                        {
                            hingeNames.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            return hingeNames;
        }


        public static int[] GetIndexForFrameHingeAssisngment(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            int[] ints = new int[2];


            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Equals("$ LOAD PATTERNS", StringComparison.OrdinalIgnoreCase))
                {
                    ints[0] = i - 1; // the line where it starts
                    ints[1] = 0; // bool value
                }
            }


            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Equals("$ FRAME HINGE ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
                {

                    ints[0] = i; // the line where it starts
                    ints[1] = 1; // bool value
                }
            }

            return ints; // the line right below it
        }

        public static void InsertLineBeforeAreaAssigns(string filePath, string newLine)
        {
            var lines = new List<string>(File.ReadAllLines(filePath));
            int[] index = GetIndexForFrameHingeAssisngment(filePath);

            if (index[1] == 1)
            {
                lines.Insert(index[0] + 1, newLine);
                File.WriteAllLines(filePath, lines);
            }
            else if (index[1] == 0)
            {

                lines.Insert(index[0] + 1, "$ FRAME HINGE ASSIGNMENTS\n" + newLine);
                File.WriteAllLines(filePath, lines);
            }
        }

        public static void RemoveFrameHingeAssignments(string filePath)
        {
            var lines = File.ReadAllLines(filePath).ToList();
            int startIndex = -1;
            int endIndex = -1;

            // Find the "$ FRAME HINGE ASSIGNMENTS" header
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Equals("$ FRAME HINGE ASSIGNMENTS", StringComparison.OrdinalIgnoreCase))
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
                return; // nothing to remove

            // Find the end (next section starting with $ or end of file)
            for (int i = startIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].StartsWith("$") && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    endIndex = i;
                    break;
                }
            }

            if (endIndex == -1)
                endIndex = lines.Count;

            // Remove the block
            lines.RemoveRange(startIndex, endIndex - startIndex);

            // Save back to file
            File.WriteAllLines(filePath, lines);
        }

        public static void UpdateNonlinearLoadcase(string filePath, string newFunc, double newSF)
        {
            // Read the file
            string text = File.ReadAllText(filePath);

            // Match the exact LOADCASE "IDA" line with FUNC and SF
            string pattern = @"(  LOADCASE\s+""IDA""\s+ACCEL\s+""U1""\s+FUNC\s+"")(.*?)(""\s+SF\s+)([\d\.]+)";

            // Replace function name and SF
            string replacement = $"$1{newFunc}\"  SF  {newSF}";

            string newText = Regex.Replace(text, pattern, replacement);

            // Overwrite file
            File.WriteAllText(filePath, newText);
        }

        public static void UpdatePushoverLoadcase(string filePath, double newDisplMag, string newStory)
        {
            // Read entire file
            string text = File.ReadAllText(filePath);

            // Regex pattern:
            // - Capture everything up to the last story name
            // - Capture the current story name
            string pattern = @"(  LOADCASE\s+""PUSHOVER""\s+LOADCONTROL\s+""Displacement""\s+DISPLTYPE\s+""Monitored""\s+MONITOREDDISPL\s+""Joint""\s+DISPLMAG\s+[\d\.]+\s+DOF\s+""U1""\s+JOINT\s+""\d+""\s+"")(.*?)("")";

            // Replacement:
            // $1 = everything before story name
            // insert new story
            // $3 = closing quote
            string replacement = $"$1{newStory}$3";

            // Replace in text
            string newText = Regex.Replace(text, pattern, replacement);

            // Overwrite file
            File.WriteAllText(filePath, newText);
        }

        public static void LoadSoilProfilesFromFile(string filePath, ref List<SoilProfile> profiles)
        {
            if (!File.Exists(filePath)) return;
            if (profiles == null) profiles = new List<SoilProfile>();

            string numPattern = @"([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)";
            bool isInsideSoilSection = false;

            // ReadLines is used for better performance with large ETABS text files
            foreach (string line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();

                // 1. Identification of the target section
                if (trimmed.StartsWith("$ SOIL PROFILES", StringComparison.OrdinalIgnoreCase))
                {
                    isInsideSoilSection = true;
                    continue;
                }

                // 2. Break if we hit the next section (starts with a new $)
                if (isInsideSoilSection && trimmed.StartsWith("$") && !trimmed.StartsWith("$ SOIL PROFILES"))
                    break;

                if (!isInsideSoilSection || string.IsNullOrWhiteSpace(trimmed)) continue;

                // 3. Extract Profile Name
                var nameMatch = Regex.Match(trimmed, "\"([^\"]*)\"");
                if (!nameMatch.Success) continue;
                string profileName = nameMatch.Groups[1].Value;

                // Find existing or create new profile entry
                var profile = profiles.FirstOrDefault(p => p.ProfileName == profileName);
                if (profile == null)
                {
                    profile = new SoilProfile { ProfileName = profileName };
                    profiles.Add(profile);
                }

                // 4. Data Extraction Logic
                if (trimmed.Contains("LAYER"))
                {
                    profile.Layers.Add(new SoilLayer
                    {
                        LayerName = Regex.Match(trimmed, "LAYER \"([^\"]*)\"").Groups[1].Value,
                        Elevation = GetVal(trimmed, "ELEVTOP", numPattern),
                        UnitWeight = GetVal(trimmed, "UNITWEIGHT", numPattern),
                        ShearModulus = GetVal(trimmed, "SHEARMODULUS", numPattern),
                        PoissonsRatio = GetVal(trimmed, "POISSONSRATIO", numPattern),
                        Cohesion = GetVal(trimmed, "COHESION", numPattern),
                        FrictionAngle = GetVal(trimmed, "FRICTIONANGLE", numPattern),
                        ShearWaveVelocity = GetVal(trimmed, "SHEARVELOCITY", numPattern)
                    });
                }
                else
                {
                    profile.ShearModulusReductionFactor = GetVal(trimmed, "SHEARMODREDUCTIONFACTOR", numPattern);
                    profile.HystereticDampingRatio = GetVal(trimmed, "HYSTERETICDAMPINGRATIO", numPattern);
                }
            }
        }

        // Static helper for regex value extraction
        private static double GetVal(string text, string key, string pattern)
        {
            var match = Regex.Match(text, $@"{key}\s+{pattern}", RegexOptions.IgnoreCase);
            return match.Success ? double.Parse(match.Groups[1].Value) : 0.0;
        }

    }

    public class SpringInfo
    {
        public string Name { get; set; }

        /// <summary>Array of 6 spring stiffness values: [U1, U2, U3, R1, R2, R3].</summary>
        public double[] K_Values { get; set; } = new double[6];

        // Log-Normal distribution parameters for tracking the input space
        public double LogNormalMedian_T { get; set; }
        public double LogNormalBeta_T { get; set; }
        public double LogNormalMedian_R { get; set; }
        public double LogNormalBeta_R { get; set; }

        // Helper property to access the Translational Stiffnesses for clarity
        public double Ku1 => K_Values[0];
        public double Ku2 => K_Values[1];
        public double Ku3 => K_Values[2];

        /// <summary>
        /// Randomly selects a spring from the provided list and assigns it to all points on the specified story.
        /// </summary>
        /// <param name="sapModel">The active ETABS/SAP2000 API object.</param>
        /// <param name="availableSprings">The list of SpringInfo objects to choose from.</param>
        /// <param name="storyName">The story name to assign springs to (default is "Base").</param>
        public static void AssignRandomSpringToStory(ref cSapModel sapModel, SpringInfo selectedSpring, string storyName = "Base")
        {
            
            
            Console.WriteLine($"Randomly selected single spring: **{selectedSpring.Name}**");
            Console.WriteLine($"Stiffness: U1={selectedSpring.Ku1}, U2={selectedSpring.Ku2}, U3={selectedSpring.Ku3}");

            


            // 3. Find all Points on the specified Story
            int pointCount = 0;
            string[] pointNames = null;

            sapModel.PointObj.GetNameListOnStory(storyName, ref pointCount, ref pointNames);

            if (pointCount == 0)
            {
                Console.WriteLine($"! WARNING: No joints found on the '{storyName}' story. Skipping assignment.");
                return;
            }

            // Ensure the model is unlocked
            sapModel.SetModelIsLocked(false);

            // 4. Assign the Selected Spring Property
            int assignmentCount = 0;
            Console.WriteLine($"Assigning spring to {pointNames.Length} joints...");

            foreach (string jointName in pointNames)
            {
                // SetSpringAssignment is preferred for assigning Named Properties
                int retVal = sapModel.PointObj.SetSpringAssignment(jointName, selectedSpring.Name);

                if (retVal == 0)
                {
                    assignmentCount++;
                }
                else
                {
                    Console.WriteLine($"! ERROR: Could not assign spring {selectedSpring.Name} to joint {jointName}. Error Code: {retVal}");
                }
            }

            Console.WriteLine($"Successfully assigned spring **{selectedSpring.Name}** to {assignmentCount} joints on story '{storyName}'.");
        }

    }

    public class SoilLayer
    {
        /// <summary>The unique identifier or name of the soil layer.</summary>
        public string LayerName { get; set; }

        /// <summary>The elevation of the top of the layer (mm).</summary>
        public double Elevation { get; set; }

        /// <summary>The unit weight of the soil (N/mm³).</summary>
        public double UnitWeight { get; set; }

        /// <summary>The shear modulus of the soil (N/mm²).</summary>
        public double ShearModulus { get; set; }

        /// <summary>The Poisson's ratio (dimensionless).</summary>
        public double PoissonsRatio { get; set; }

        /// <summary>The cohesion of the soil (N/mm²).</summary>
        public double Cohesion { get; set; }

        /// <summary>The internal friction angle (degrees).</summary>
        public double FrictionAngle { get; set; }

        /// <summary>The shear wave velocity (mm/sec).</summary>
        public double ShearWaveVelocity { get; set; }

        /// <summary>The hexadecimal or ARGB color code for the layer visualization.</summary>
        public string LayerColor { get; set; }
    }

    public class SoilProfile
    {
        /// <summary>The name of the soil profile (e.g., "SoilPr1").</summary>
        public string ProfileName { get; set; }

        /// <summary>Collection of soil layers making up the profile.</summary>
        public List<SoilLayer> Layers { get; set; } = new List<SoilLayer>();

        /// <summary>Factor for large strain effects (dimensionless).</summary>
        public double ShearModulusReductionFactor { get; set; } = 1.0;

        /// <summary>The hysteretic damping ratio of the soil.</summary>
        public double HystereticDampingRatio { get; set; } = 0.5;


        public static void CalculateRandomSoilSprings(ref cSapModel cSapModel, List<SoilProfile> profiles, double L, double B, ref SpringInfo springData, ref SoilProfile selectedProfile)
        {
            if (profiles == null || profiles.Count == 0)
            {
                Console.WriteLine("No soil profiles found. Please check your text file or parsing logic.");
                return;
            }

            if (springData == null) springData = new SpringInfo();

            // 1. Pick a random profile from the parsed list
            Random rnd = new Random();
            int index = rnd.Next(profiles.Count);
            selectedProfile = profiles[index];

            Console.WriteLine($"--- Randomly Selected Profile: {selectedProfile.ProfileName} ---");

            // 2. Calculate stiffness for each layer in this profile
            // Note: For fragility analysis, we usually focus on the primary interacting layer
            foreach (var layer in selectedProfile.Layers)
            {
                double G = layer.ShearModulus;
                double v = layer.PoissonsRatio;

                // Ensure L is the larger dimension for Gazetas formula consistency
                double length = Math.Max(L, B);
                double width = Math.Min(L, B);


                int pointCount = 0;
                string[] pointNames = null;

                // 1. Get all points on the Base story
                int ret = cSapModel.PointObj.GetNameListOnStory("Base", ref pointCount, ref pointNames);

                double area = width * length;

                double R_translation = Math.Sqrt(area / Math.PI);
                double R_rocking = Math.Pow((width * Math.Pow(length, 3)) / (3 * Math.PI), 0.25);



                // Vertical Stiffness (Kz)
                springData.K_Values[2] = Math.Abs((4 * G * R_translation) / (1 - v) / pointCount);

                // Horizontal Stiffness (Kh)
                springData.K_Values[1] = Math.Abs((8 * G * R_translation) / (2 - v) / pointCount);
                springData.K_Values[0] = Math.Abs((8 * G * R_translation) / (2 - v) / pointCount);

                // Rocking Stiffness (K_theta)
                // I = Moment of Inertia for a rectangular base around the axis of rotation
                double I = (length * Math.Pow(width, 3)) / 12;
                springData.K_Values[3] = Math.Abs((8 * G * Math.Pow(R_rocking, 3)) / (3 * (1 - v)) / pointCount);
                springData.K_Values[4] = Math.Abs((8 * G * Math.Pow(R_rocking, 3)) / (3 * (1 - v)) / pointCount);
                springData.K_Values[5] = Math.Abs((8 * G * Math.Pow(R_rocking, 3)) / (3 * (1 - v)) / pointCount);

                double[] doubles = new double[6];
                doubles = springData.K_Values;

                cSapModel.PropPointSpring.SetPointSpringProp(springData.Name, 1, ref doubles);

                // --- Output to Console ---
                Console.WriteLine($"Layer: {layer.LayerName}");
                Console.WriteLine($"  Vertical (Kz):   {springData.Ku3:E2} N/mm");
                Console.WriteLine($"  Horizontal (Kh): {springData.Ku1:E2} N/mm");
                Console.WriteLine($"  Rocking (Kth):   {springData.Ku3:E2} N-mm/rad");
                Console.WriteLine("------------------------------------------");
            }
        }

        public static void GetFoundationDimensions(ref cSapModel sapModel, string storyName, ref double L, ref double B)
        {
            L = 0;
            B = 0;
            int pointCount = 0;
            string[] pointNames = null;

            // 1. Get all points on the Base story
            int ret = sapModel.PointObj.GetNameListOnStory(storyName, ref pointCount, ref pointNames);

            if (ret == 0 && pointCount > 0)
            {
                double xMin = double.MaxValue;
                double xMax = double.MinValue;
                double yMin = double.MaxValue;
                double yMax = double.MinValue;

                // 2. Determine the coordinate extremes
                foreach (string ptName in pointNames)
                {
                    double x = 0, y = 0, z = 0;
                    sapModel.PointObj.GetCoordCartesian(ptName, ref x, ref y, ref z);

                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }

                // 3. Calculate total footprint dimensions
                double widthX = Math.Abs(xMax - xMin);
                double widthY = Math.Abs(yMax - yMin);

                // 4. Assign L as the larger and B as the smaller for Gazetas formula consistency
                L = Math.Max(widthX, widthY);
                B = Math.Min(widthX, widthY);
            }
            else
            {
                Console.WriteLine($"Error: Could not find points on story '{storyName}'. Check the story name.");
            }
        }
    }

}


