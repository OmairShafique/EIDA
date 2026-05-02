using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ETABSv1;
using MLApp;
using System.Diagnostics;
using ETABSPlugInAPI;
using System.Windows.Forms.VisualStyles;
using static ETABSPlugInAPI.MaterialInfo;
using System.Xml.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;
using System.Security.Policy;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
using System.Drawing.Drawing2D;

namespace ETABs
{
    internal class Program
    {
        #region Extra Functions for taking screenshots

        #region Taking Picture Logic
        // Import necessary Win32 API functions
        [DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

        // Structure to hold rectangle information
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static Bitmap CaptureWindow(string windowTitle)
        {
            // Find the window by its title
            IntPtr hWnd = FindWindow(null, windowTitle);

            if (hWnd == IntPtr.Zero)
            {
                Console.WriteLine($"Window '{windowTitle}' not found.");
                return null;
            }

            // Get the window's dimensions
            RECT rect;
            GetWindowRect(hWnd, out rect);

            // Create a bitmap to store the screenshot
            Bitmap bmp = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top, PixelFormat.Format32bppArgb);

            // Create a Graphics object from the bitmap
            using (Graphics gfx = Graphics.FromImage(bmp))
            {
                IntPtr hdcBitmap = gfx.GetHdc(); // Get the device context of the bitmap

                // Print the window's content to the bitmap
                PrintWindow(hWnd, hdcBitmap, 0);

                gfx.ReleaseHdc(hdcBitmap); // Release the device context
            }

            return bmp;
        }
        #endregion

        #region Window List

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        static List<string> GetVisibleWindowTitles()
        {
            List<string> windowTitles = new List<string>();

            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    if (title.Length > 0)
                        windowTitles.Add(title.ToString());
                }
                return true; // continue enumeration
            }, IntPtr.Zero);

            return windowTitles;
        }


        #endregion

        #region Activate Windw

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);



        enum SystemMetric
        {
            SM_CXSCREEN = 0,
            SM_CYSCREEN = 1,
        }

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(SystemMetric smIndex);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

        const int SW_RESTORE = 9;
        const int SW_MAXIMIZE = 3;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        static void ActivateWindowByTitle(string windowTitle)
        {
            IntPtr hwnd = FindWindow(null, windowTitle);
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"Window \"{windowTitle}\" not found.");
                return;
            }

            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);

            // Get primary screen dimensions
            int screenX = 0;
            int screenY = 0;
            int screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);

            // Move window to primary screen (0,0) and resize it to full screen
            SetWindowPos(hwnd, IntPtr.Zero, screenX, screenY, screenWidth, screenHeight, SWP_NOZORDER | SWP_NOACTIVATE);

            // Maximize the window
            ShowWindow(hwnd, SW_MAXIMIZE);
        }


        #endregion

        #endregion

        [STAThread] // Required for UI dialogs in a console app
        static void Main(string[] args)
        {
            MLApp.MLApp matlab = null;
            cOAPI etabsObject = null;
            cSapModel sapModel = null;
            try
            {
                #region Initial Work

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                string filePath = string.Empty;


                // Open a Windows Explorer dialog to select the ETABS model file
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.InitialDirectory = "C:\\";
                    openFileDialog.Filter = "ETABS Files (*.edb)|*.edb|All Files (*.*)|*.*";
                    openFileDialog.Title = "Select ETABS Model File";
                    openFileDialog.CheckFileExists = true;
                    openFileDialog.CheckPathExists = true;
                    openFileDialog.RestoreDirectory = true;
                    openFileDialog.Multiselect = false;
                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        filePath = openFileDialog.FileName;
                        Console.WriteLine($"File Selection: {filePath}");
                    }
                    else
                    {
                        Console.WriteLine("No file selected. Exiting...");
                        return;
                    }
                }




                // Create helper object
                cHelper helper = new Helper();

                // Start ETABS
                etabsObject = helper.CreateObjectProgID("CSI.ETABS.API.ETABSObject");
                etabsObject.ApplicationStart();


                Console.WriteLine("ETABS application started");
                //etabsObject.Hide();

                //Console.WriteLine("ETABS application hidden");

                // Get model object
                sapModel = etabsObject.SapModel;

                Console.WriteLine("\nOpening Model File");

                // Open the ETABS model file
                if (!string.IsNullOrEmpty(filePath))
                {
                    sapModel.File.OpenFile(filePath);
                    Console.WriteLine($"Model file opened: {filePath}");
                }
                else
                {
                    Console.WriteLine("No file path provided. Exiting...");
                    return;
                }
                // Getting the Filename
                string fileName_etabs = Path.GetFileNameWithoutExtension(filePath);

                string e2tFolder = Directory.GetParent(filePath).FullName;

                string e2tFilePath = Path.Combine(e2tFolder, fileName_etabs + ".$et");



                sapModel.SetModelIsLocked(false);

                sapModel.SetPresentUnits(eUnits.N_mm_C);

                


                // Initialize model
                //sapModel.InitializeNewModel(eUnits.kN_m_C);

                // Perform data extraction or other

                #endregion

                #region Data Extraction

                #region 0 Grid

                Grid grid = Grid.CreateFromETABS(sapModel);

                #endregion

                #region 1  Create a single list for each property type

                List<MaterialInfo> materialList = new List<MaterialInfo>();
                List<FrameSectionInfo> frameSectionList = new List<FrameSectionInfo>();
                List<AreaSectionInfo> areaSectionList = new List<AreaSectionInfo>();
                List<LoadPatternInfo> loadPatternList = new List<LoadPatternInfo>();
                List<LoadCombinationInfo> loadCombinationList = new List<LoadCombinationInfo>();
                List<LoadCasesInfo> loadCasesInfos = new List<LoadCasesInfo>();
                List<Function> functions = new List<Function>();

                List<FrameObject> frameObjectList = new List<FrameObject>();
                List<AreaObject> areaObjectList = new List<AreaObject>();
                List<JointObject> jointObjectList = new List<JointObject>();

                List<Story> storyList = new List<Story>();
                List<Hinge> hinges = new List<Hinge>();
                List<SpringInfo> base_springs = new List<SpringInfo>();

                List<SoilProfile> soil_profiles = new List<SoilProfile>();

                double Length = 0;
                double Bredth = 0;

                #endregion

                #region 1.5 Getting all Table Data

                // Provide the path to your file

                #endregion

                #region 2 Importing Material Properties

                // --- 1. Material Properties ---
                int matCount = 0;
                string[] matNames = null;
                sapModel.PropMaterial.GetNameList(ref matCount, ref matNames);
                if (matCount > 0)
                {
                    foreach (string name in matNames)
                    {
                        var material = new MaterialInfo { Name = name };
                        var tempMaterialType = eMatType.NoDesign;
                        var tempMaterialColor = 0; // Default color


                        string notes = "", guid = "";



                        // General properties
                        sapModel.PropMaterial.GetMaterial(name, ref tempMaterialType, ref tempMaterialColor, ref notes, ref guid);
                        double w = 0, m = 0;
                        sapModel.PropMaterial.GetWeightAndMass(name, ref w, ref m);
                        material.UnitWeight = w;

                        // Set the material properties
                        material.MaterialType = tempMaterialType;
                        material.Color = tempMaterialColor;
                        material.Notes = notes;



                        // Mechanical properties
                        double[] e = new double[1], u = new double[1], a = new double[1], g = new double[1];
                        sapModel.PropMaterial.GetMPAnisotropic(name, ref e, ref u, ref a, ref g);
                        material.ElasticModulus = e;
                        material.PoissonsRatio = u;
                        material.ThermalCoeff = a;
                        material.SheerModulus = g;

                        // Type-specific strength properties
                        if (material.MaterialType == eMatType.Concrete)
                        {
                            double fc = 0, fcsFactor = 0, strainAtFc = 0, strainUltimate = 0, frictionAngle = 0, dilAngle = 0;
                            bool isLightweight = false;
                            int ssType = 0, ssHysType = 0;


                            sapModel.PropMaterial.GetOConcrete(name, ref fc, ref isLightweight, ref fcsFactor, ref ssType, ref ssHysType,
                                                                ref strainAtFc, ref strainUltimate, ref frictionAngle, ref dilAngle);
                            // Populate
                            material.FcPrime = fc;
                            material.IsLightweight = isLightweight;
                            material.FcsFactor = fcsFactor;
                            material.ConcreteStressStrainType = (StressStrainType)ssType;
                            material.ConcreteHysteresisType = (HysteresisType)ssHysType;
                            material.StrainAtFc = strainAtFc;
                            material.StrainUltimate = strainUltimate;
                            material.FrictionAngle = frictionAngle;
                            material.DilationAngle = dilAngle;

                        }
                        else if (material.MaterialType == eMatType.Steel)
                        {
                            double fy = 0, fu = 0, efy = 0, efu = 0;
                            int ssType = 0, ssHysType = 0;
                            double strainAtHardening = 0, strainAtMaxStress = 0, strainAtRupture = 0;

                            int ret = sapModel.PropMaterial.GetOSteel(
                                name,
                                ref fy, ref fu,
                                ref efy, ref efu,
                                ref ssType, ref ssHysType,
                                ref strainAtHardening,
                                ref strainAtMaxStress,
                                ref strainAtRupture
                            );

                            // Store in object
                            material.Fy = fy;
                            material.Fu = fu;
                            material.EFy = efy;
                            material.EFu = efu;

                            material.SteelStressStrainType = (StressStrainType)ssType;
                            material.SteelHysteresisType = (HysteresisType)ssHysType;

                            material.StrainAtHardening = strainAtHardening;
                            material.StrainAtMaxStress = strainAtMaxStress;
                            material.StrainAtRupture = strainAtRupture;
                        }

                        materialList.Add(material);
                    }
                }

                #endregion

                #region 3 Importing Frame Section Properties

                // --- 2. Frame Section Properties ---
                int frameCount = 0;
                string[] frameNames = null;
                sapModel.PropFrame.GetNameList(ref frameCount, ref frameNames);
                if (frameCount > 0)
                {
                    foreach (string name in frameNames)
                    {
                        var section = new FrameSectionInfo { Name = name };
                        string notes = "", guid = "";


                        // Get Modifiers
                        double[] modifiers = new double[8];
                        sapModel.PropFrame.GetModifiers(name, ref modifiers);
                        section.AreaMod = modifiers[0];
                        section.Shear22Mod = modifiers[1];
                        section.Shear33Mod = modifiers[2];
                        section.TorsionMod = modifiers[3];
                        section.I22Mod = modifiers[4];
                        section.I33Mod = modifiers[5];
                        section.MassMod = modifiers[6];
                        section.WeightMod = modifiers[7];

                        MaterialInfo tempmaterial = new MaterialInfo();
                        string tempmatname = "";
                        double T3 = 0, T2 = 0; // T3 = Total Depth; T2 = Total Width for Concrete, top flange width for steel;
                        int tempcolor = 0;
                        string tempfilename = "";
                        double Tf = 0; // the top flange thickness
                        double Tw = 0; // the web thicnkness
                        double T2b = 0; // The bottom flange width
                        double Tfb = 0; // the bottom flange thickness

                        // Get Dimensions (check for different shapes)
                        if (sapModel.PropFrame.GetRectangle(name, ref tempfilename, ref tempmatname, ref T3, ref T2,
                                                                                    ref tempcolor, ref notes, ref guid) == 0)
                        {
                            section.Shape = "Rectangular";
                            section.Material = materialList.FirstOrDefault(m => m.Name == tempmatname);
                            section.Depth = T3;
                            section.Width = T2;
                        }
                        else if (sapModel.PropFrame.GetISection(name, ref tempfilename, ref tempmatname, ref T3, ref T2, ref Tf
                                                                                    , ref Tw, ref T2b, ref Tfb, ref tempcolor, ref notes, ref guid) == 0)

                        {
                            section.Shape = "I-Section";
                            section.Material = materialList.FirstOrDefault(m => m.Name == tempmatname);
                            section.Depth = T3;
                            section.Width = T2;
                            section.FlangeThickness = Tf;
                            section.WebThickness = Tw;
                        }
                        else
                        {
                            // Fallback: shape unknown, just get material name
                            string fallbackMatName = "";
                            sapModel.PropFrame.GetMaterial(name, ref fallbackMatName);
                            section.Material = materialList.FirstOrDefault(m => m.Name == fallbackMatName);
                            section.Shape = "Other/Unknown";
                        }

                        frameSectionList.Add(section);
                    }
                }

                #endregion

                #region 4 Importing Area Section Properties


                // --- 3. Area Section Properties ---
                int areaCount = 0;
                string[] areaNames = null;
                sapModel.PropArea.GetNameList(ref areaCount, ref areaNames);



                if (areaCount > 0)
                {
                    foreach (string name in areaNames)
                    {
                        var section = new AreaSectionInfo { Name = name };
                        eSlabType slabType = eSlabType.Drop;
                        eShellType shellType = eShellType.ShellThin; // Default shell type
                        string tempmatname = ""; // Initialize to avoid null reference
                        double Thickness = 0; // Default thickness
                        int color = 0;
                        string notes = "", guid = "";

                        eWallPropType eWallPropType = eWallPropType.AutoSelectList; // Default wall type


                        if (sapModel.PropArea.GetSlab(name, ref slabType, ref shellType, ref tempmatname, ref Thickness, ref color, ref notes, ref guid) == 0)
                        {
                            section.AreaType = "Slab";
                            section.SlabType = slabType;
                            section.ShellType = shellType;
                            section.MaterialName = materialList.FirstOrDefault(m => m.Name == tempmatname);
                            section.Thickness = Thickness;

                        }
                        else if (sapModel.PropArea.GetWall(name, ref eWallPropType, ref shellType, ref tempmatname
                                                                        , ref Thickness, ref color, ref notes, ref guid) == 0)
                        {
                            section.AreaType = "Wall";
                            section.WallPropType = eWallPropType;
                            section.ShellType = shellType;
                            section.MaterialName = materialList.FirstOrDefault(
                                m => m.Name == tempmatname);
                            section.Thickness = Thickness;

                        }
                        else
                        {
                            section.AreaType = "Other";
                            section.MaterialName = new MaterialInfo();
                            section.MaterialName.Name = tempmatname; // Fallback to just material name
                        }

                        areaSectionList.Add(section);
                    }
                }

                #endregion

                #region 5 Importing Load Patterns

                // --- 4. Load Patterns ---
                int loadPatternCount = 0;
                string[] loadPatternNames = null;
                sapModel.LoadPatterns.GetNameList(ref loadPatternCount, ref loadPatternNames);


                if (loadPatternCount > 0)
                {
                    foreach (string name in loadPatternNames)
                    {
                        var pattern = new LoadPatternInfo { Name = name };
                        eLoadPatternType eLoadPatternType = eLoadPatternType.PatternAuto;
                        double selfWeightMultiplier = 0;

                        sapModel.LoadPatterns.GetLoadType(name, ref eLoadPatternType);
                        pattern.PatternType = eLoadPatternType;

                        sapModel.LoadPatterns.GetSelfWTMultiplier(name, ref selfWeightMultiplier);
                        pattern.SelfWeightMultiplier = selfWeightMultiplier;

                        string autoLoadName = "";
                        sapModel.LoadPatterns.GetAutoSeismicCode(name, ref autoLoadName);
                        if (string.IsNullOrEmpty(autoLoadName))
                        {
                            sapModel.LoadPatterns.GetAutoWindCode(name, ref autoLoadName);
                        }

                        if (!string.IsNullOrEmpty(autoLoadName))
                        {
                            pattern.IsAutoLoad = true;
                            pattern.AutoLoadType = autoLoadName;
                        }

                        loadPatternList.Add(pattern);
                    }
                }


                #endregion

                #region 5.5 Importing Load Cases

                // Get all load case names
                // Get all load case names
                int loadCaseCount = 0;
                string[] loadCaseNames = null;
                sapModel.LoadCases.GetNameList(ref loadCaseCount, ref loadCaseNames);


                if (loadCaseCount > 0)
                {
                    foreach (string name in loadCaseNames)
                    {
                        // Get the type of the load case
                        eLoadCaseType loadCaseType = 0;
                        int subtype = 0;
                        sapModel.LoadCases.GetTypeOAPI(name, ref loadCaseType, ref subtype);

                        // 1. Check if the current load case is a Time History case
                        if (loadCaseType == eLoadCaseType.LinearHistory || loadCaseType == eLoadCaseType.NonlinearHistory)
                        {
                            Console.WriteLine($"\nFound Time History Case: {name}");

                            // 2. Declare variables to hold the "Loads Applied" data
                            int numberLoads = 0;
                            string[] loadType = null;
                            string[] loadName = null;
                            string[] function = null;
                            double[] scaleFactor = null;
                            double[] timeFactor = null;
                            double[] arrivalTime = null;
                            string[] coordSystem = null;
                            double[] angle = null;

                            // 3. Call the specific method to get the loads for a Time History case
                            sapModel.LoadCases.ModHistLinear.GetLoads(name, ref numberLoads, ref loadType,
                                ref loadName, ref function, ref scaleFactor, ref timeFactor, ref arrivalTime,
                                ref coordSystem, ref angle);

                            var loadcase = new LoadCasesInfo
                            {
                                Name = name,
                                LoadCaseType = loadCaseType,
                                SubTypeInt = subtype,
                                numberOfLoads = numberLoads,
                                Loadtype = loadType,
                                LoadName = loadName,
                                function = function,
                                scaleFactors = scaleFactor,
                                timeFactor = timeFactor,
                                arrivalTime = arrivalTime,
                                coordSystem = coordSystem,
                                angle = angle
                            };

                            loadCasesInfos.Add(loadcase);
                            //sapModel.LoadCases.ModHistLinear.SetLoads(name, numberLoads, ref loadType, ref loadName, ref function, ref scaleFactor, ref timeFactor, ref arrivalTime, ref  coordSystem,ref angle);
                        }
                        else
                        {
                            var loadcase = new LoadCasesInfo
                            {
                                Name = name,
                                LoadCaseType = loadCaseType,
                                SubTypeInt = subtype,
                            };
                            loadCasesInfos.Add(loadcase);

                        }
                    }
                }


                #endregion

                #region 5.6 Saving User Functions for Accelerograms

                // --- 5. Saving User Functions for Accelerograms ---
                int userFunctionCount = 0;
                string[] userFunctionNames = null;
                sapModel.Func.GetNameList(ref userFunctionCount, ref userFunctionNames);
                double[] temp_times = { 0 };
                double[] temp_values = { 0 };


                if (userFunctionCount > 0)
                {
                    foreach (string name in userFunctionNames)
                    {
                        // Get the function data
                        int tempfunctype = 0, tempaddtype = 0;
                        sapModel.Func.GetTypeOAPI(name, ref tempfunctype, ref tempaddtype);
                        sapModel.Func.GetValues(name, ref userFunctionCount, ref temp_times, ref temp_values);

                        // Create a new UserFunctionInfo object
                        var functionInfo = new Function()
                        {
                            Name = name,
                            FuncTypeInt = tempfunctype,
                            AddTypeInt = tempaddtype,
                            TimeValues = temp_times,
                            Value = temp_values
                        };

                        // Add to the list (if you have a list for user functions)
                        functions.Add(functionInfo);
                    }
                }

                #endregion

                #region 6 Importing Load Combinations 

                // --- 5. Load Combinations ---
                int comboCount = 0;
                string[] comboNames = null;
                sapModel.RespCombo.GetNameList(ref comboCount, ref comboNames);
                if (comboCount > 0)
                {
                    foreach (string name in comboNames)
                    {
                        var combo = new LoadCombinationInfo { Name = name };
                        int numberItems = 0;
                        eCNameType[] caseTypes = null;
                        string[] caseNames = null;
                        double[] scaleFactors = null;
                        int comboType = 0;

                        sapModel.RespCombo.GetTypeCombo(name, ref comboType);
                        combo.ComboType = (LoadCombinationInfo.MyComboType)comboType;

                        sapModel.RespCombo.GetCaseList(name, ref numberItems, ref caseTypes, ref caseNames, ref scaleFactors);

                        for (int i = 0; i < numberItems; i++)
                        {
                            combo.Cases.Add(new LoadCombinationInfo.ComboCase
                            {
                                CaseType = caseTypes[i],
                                CaseName = caseNames[i],
                                ScaleFactor = scaleFactors[i]
                            });
                        }
                        loadCombinationList.Add(combo);
                    }
                }

                #endregion

                #region 8 Joints

                // -- 6. Joints and Frame/Area Objects (Optional) ---
                int jointCount = 0;
                string[] jointNames = null;

                sapModel.PointObj.GetNameList(ref jointCount, ref jointNames);

                if (jointCount > 0)
                {
                    foreach (string name in jointNames)
                    {
                        double x = 0, y = 0, z = 0;
                        bool[] constraint = new bool[5]; // Default constraint value
                        string guid = "";

                        sapModel.PointObj.GetCoordCartesian(name, ref x, ref y, ref z);
                        sapModel.PointObj.GetRestraint(name, ref constraint);
                        sapModel.PointObj.GetGUID(name, ref guid);
                        var joint = new JointObject
                        {
                            Name = name,
                            X = x,
                            Y = y,
                            Z = z,
                            Constraint = constraint,
                            GUID = guid
                        };
                        jointObjectList.Add(joint);
                    }
                }
                for (int i = 0;
                        i < jointCount; i++)
                {
                    // Display joint information
                    Console.WriteLine($"Joint {i + 1}: {jointNames[i]} at ({jointObjectList[i].X}, {jointObjectList[i].Y}, {jointObjectList[i].Z})");
                    Console.WriteLine($"  Constraints: {string.Join(", ", jointObjectList[i].Constraint.Select(c => c.ToString()))}");
                    Console.WriteLine($"  GUID: {jointObjectList[i].GUID}");
                }

                #endregion

                #region 9 Frame Objects

                Importing_Frame_Objects(sapModel, frameSectionList, frameObjectList);

                #endregion

                #region 10 Area Objects - Need to add more types of Slabs

                int areaObjectCount = 0;
                string[] areaObjectNames = null;
                sapModel.AreaObj.GetNameList(ref areaObjectCount, ref areaObjectNames);


                if (areaObjectCount > 0)
                {
                    foreach (var name in areaObjectNames)
                    {
                        string areaSection = "", matName = "", notes = "", guid = "";
                        double thickness = 0;
                        eSlabType slabType = eSlabType.Drop;
                        eShellType shellType = eShellType.ShellThin;
                        string tempmatName = ""; // Initialize to avoid null reference
                        eWallPropType wallPropType = eWallPropType.Specified;
                        eDeckType deckType = eDeckType.Unfilled; // Default deck type
                        int color = 0;

                        int NumberOfPoints = 0;
                        string[] points = null;



                        sapModel.AreaObj.GetProperty(name, ref areaSection);
                        var areaInfo = areaSectionList.FirstOrDefault(a => a.Name == areaSection);

                        //sapModel.AreaObj.ge

                        sapModel.AreaObj.GetMaterialOverwrite(name, ref matName);

                        if (areaInfo != null && areaInfo.AreaType == "Slab")
                        {
                            sapModel.PropArea.GetSlab(areaSection, ref slabType, ref shellType, ref tempmatName, ref thickness, ref color, ref notes, ref guid);
                        }
                        else if (areaInfo != null && areaInfo.AreaType == "Wall")
                        {
                            sapModel.PropArea.GetWall(areaSection, ref wallPropType, ref shellType, ref tempmatName, ref thickness, ref color, ref notes, ref guid);
                        }
                        else if (areaInfo != null && areaInfo.AreaType == "Deck")
                        {
                            sapModel.PropArea.GetDeck(areaSection, ref deckType, ref shellType, ref tempmatName, ref thickness, ref color, ref notes, ref guid);
                        }


                        //sapModel.AreaObj.GetLabelNotes(name, ref notes);

                        // Getting the Points
                        sapModel.AreaObj.GetPoints(name, ref NumberOfPoints, ref points);

                        var area = new AreaObject
                        {
                            Name = name,
                            sapModel = sapModel,
                            AreaType = areaInfo,
                            ShellType = shellType,
                            SlabType = (areaInfo?.AreaType == "Slab") ? (eSlabType?)slabType : null,
                            WallPropType = wallPropType,
                            MaterialName = materialList.FirstOrDefault(m => m.Name == matName),
                            Thickness = thickness,
                            PointNames = points,
                            GUID = guid,
                            Notes = notes
                        };

                        areaObjectList.Add(area);
                    }
                }

                ETABSPlugInAPI.AreaObject.PopulateStoryNames(sapModel, areaObjectList);

                #endregion

                #region 11 Stories


                // adding base story
                var b_ase = new Story
                {
                    Name = "base",
                    Height = 0,
                    Elevation = 0,
                    IsMasterStory = false,
                    SimilarToStory = "Story1",
                    SpliceAbove = false,
                    SpliceHeight = 0,
                    color = 0,
                };

                storyList.Add(b_ase);


                double baseElevation = 0;
                int numStories1 = 0;
                string[] storyNames1 = null;
                double[] storyElevations1 = null;
                double[] storyHeights1 = null;
                bool[] isMasterStory1 = null;
                string[] similarToStory1 = null;
                bool[] spliceAbove1 = null;
                double[] spliceHeight1 = null;
                int[] color12 = null;

                // Get all story properties at once
                sapModel.Story.GetStories_2(ref baseElevation, ref numStories1, ref storyNames1, ref storyElevations1,
                                            ref storyHeights1, ref isMasterStory1, ref similarToStory1,
                                            ref spliceAbove1, ref spliceHeight1, ref color12);

                // --- 2. Populate the List from the Retrieved Arrays ---

                // Loop through the arrays and create a Story object for each index
                for (int i = 0; i < numStories1; i++)
                {
                    var story = new Story
                    {
                        Name = storyNames1[i],
                        Height = storyHeights1[i],
                        Elevation = storyElevations1[i],
                        IsMasterStory = isMasterStory1[i],
                        SimilarToStory = similarToStory1[i],
                        SpliceAbove = spliceAbove1[i],
                        SpliceHeight = spliceHeight1[i],
                        color = color12[i],
                    };

                    storyList.Add(story);
                }



                #endregion

                #region 12 Hinges

                // Save the Table List to a Text File

                var hinges_list = Parsing.ExtractFrameHingeNames(e2tFilePath);
                hinges_list = hinges_list.Distinct().ToList();

                Console.WriteLine($"\n{hinges_list.Count} Hinges Identified:");

                foreach (var h in hinges_list)
                {
                    Hinge hinge = new Hinge();
                    hinge.Name = h;
                    hinges.Add(hinge);
                    Console.Write(" " + h);
                }

                #endregion

                #region 13 Soils

                Parsing.LoadSoilProfilesFromFile(e2tFilePath, ref soil_profiles);

                SoilProfile.GetFoundationDimensions(ref sapModel, "Base", ref Length, ref Bredth);

                #endregion

                #region 14 Base Springs Points

                Console.WriteLine("\n--- Retrieving and Assigning Base Spring Properties ---");

                // 1. Retrieve the existing list of Point Spring Properties from ETABS
                int springCount = 0;
                string[] springNames = null;
                sapModel.PropPointSpring.GetNameList(ref springCount, ref springNames);


                if (springCount > 0)
                {
                    Console.WriteLine($"Found {springCount} defined spring properties. Retrieving stiffness values...");

                    // Populate the C# list with names AND stiffness values
                    foreach (string name in springNames)
                    {
                        // Variables required for GetPointSpringProp
                        int springOption = 0;
                        double[] kValues = new double[6]; // This will hold [U1, U2, U3, R1, R2, R3]
                        string cSys = "";
                        string soilProfile = "";
                        string footing = "";
                        double period = 0;
                        int color = 0;
                        string notes = "";
                        string iGUID = "";

                        // Retrieve the specific properties (K values) for this spring name
                        int ret = sapModel.PropPointSpring.GetPointSpringProp(
                            name,
                            ref springOption,
                            ref kValues,
                            ref cSys,
                            ref soilProfile,
                            ref footing,
                            ref period,
                            ref color,
                            ref notes,
                            ref iGUID
                        );

                        if (ret == 0)
                        {
                            // Add to our list with both Name and Stiffness populated
                            base_springs.Add(new SpringInfo
                            {
                                Name = name,
                                K_Values = kValues
                            });
                        }
                    }
                }
                else
                {
                    Console.WriteLine("! WARNING: No spring properties found in the ETABS model. Skipping assignment.");
                    goto EndSpringAssignment;
                }


                //// 2. Random Selection
                //Random randomSpringSelector = new Random();
                //int randomIndex = randomSpringSelector.Next(base_springs.Count);
                SpringInfo selectedSpring = base_springs[0];

                //SoilProfile.CalculateRandomSoilSprings(ref sapModel, soil_profiles, Length, Bredth, ref selectedSpring);


                //// Assuming you have 'sapModel' and 'base_springs' defined already:

                //SpringInfo.AssignRandomSpringToStory(ref sapModel, selectedSpring, "Base");

            EndSpringAssignment:;

                #endregion

                

                #region Displaying All the Data to Console

                // --- Display All Extracted Data (Example for one property) ---

                Console.WriteLine($"\n--- Materials ({materialList.Count}) ---");
                foreach (var mat in materialList)
                {
                    Console.WriteLine($"Name: {mat.Name}, Type: {mat.MaterialType}, E: {mat.ElasticModulus}, Fc': {mat.FcPrime}");
                }

                Console.WriteLine($"\n--- Frame Sections ({frameSectionList.Count}) ---");
                foreach (var sec in frameSectionList)
                {
                    Console.WriteLine($"Name: {sec.Name}, Shape: {sec.Shape}, Material: {sec.Material.Name}, I33 Mod: {sec.I33Mod}");
                }

                // (Add similar display loops for other lists as needed)

                Console.WriteLine($"\n--- Area Sections ({areaSectionList.Count}) ---");
                foreach (var area in areaSectionList)
                {
                    Console.WriteLine($"Name: {area.Name}, Type: {area.AreaType}, Material: {area.MaterialName.Name}, Thickness: {area.Thickness}");
                }

                Console.WriteLine($"\n--- Load Patterns ({loadPatternList.Count}) ---");
                foreach (var pattern in loadPatternList)
                {
                    Console.WriteLine($"Name: {pattern.Name}, Type: {pattern.PatternType}, Auto Load: {pattern.IsAutoLoad}, Self Weight Multiplier: {pattern.SelfWeightMultiplier}");
                }

                Console.WriteLine($"\n--- Load Combinations ({loadCombinationList.Count}) ---");
                foreach (var pattern in loadPatternList)
                {
                    Console.WriteLine($"Name: {pattern.Name}, Type: {pattern.PatternType}, Auto Load: {pattern.IsAutoLoad}, Self Weight Multiplier: {pattern.SelfWeightMultiplier}");
                }

                // Display Load Combinations
                Console.WriteLine($"\n--- Load Combinations ({loadCombinationList.Count}) ---");
                foreach (var pattern in loadPatternList)
                {
                    Console.WriteLine($"Name: {pattern.Name}, Type: {pattern.PatternType}, Auto Load: {pattern.IsAutoLoad}, Self Weight Multiplier: {pattern.SelfWeightMultiplier}");
                }

                Console.WriteLine("\nData extraction completed successfully.");


                // Display the Frame objects
                Console.WriteLine("\n===== Frame Objects =====\n");
                for (int i = 0; i < frameObjectList.Count; i++)
                {
                    var frame = frameObjectList[i];
                    Console.WriteLine($"Frame {i + 1}: {frame.Name}");
                    Console.WriteLine($"  Section: {frame.SectionName?.Name ?? "Unknown"}");
                    Console.WriteLine($"  Material: {frame.MaterialName?.Name ?? "Unknown"}");
                    Console.WriteLine($"  Length: {frame.Length:0.###} m");

                    // Display the start and end points in frame.PointCoordinates
                    Console.WriteLine($"  Start Point: {frame.Points[0].Name} ({frame.Points[0].X:0.###}, {frame.Points[0].Y:0.###}, {frame.Points[0].Z:0.###})");
                    Console.WriteLine($"  End Point: {frame.Points[0].Name} ({frame.Points[1].X:0.###}, {frame.Points[1].Y:0.###}, {frame.Points[1].Z:0.###})");


                    Console.WriteLine($"  Story: {frame.Story}");
                    Console.WriteLine($"  GUID: {frame.GUID}");
                }


                // Display the Area objects

                Console.WriteLine("\n===== Area Objects =====\n");
                for (int i = 0; i < areaObjectList.Count; i++)
                {
                    var area = areaObjectList[i];
                    Console.WriteLine($"Area {i + 1}: {area.Name}");
                    Console.WriteLine($"  Type: {area.AreaType?.AreaType ?? "Unknown"}");
                    Console.WriteLine($"  Material: {area.MaterialName?.Name ?? "Unknown"}");
                    Console.WriteLine($"  Thickness: {area.Thickness:0.###} m");
                    Console.WriteLine($"  Shell Type: {area.ShellType}");
                    if (area.AreaType?.AreaType == "Slab")
                        Console.WriteLine($"  Slab Type: {area.SlabType}");
                    else if (area.AreaType?.AreaType == "Wall")
                        Console.WriteLine($"  Wall Prop Type: {area.WallPropType}");

                    // Sendign the points ot console using the Points Property
                    foreach (var Point in area.Points)
                    {
                        Console.WriteLine($"  Point: {Point.Name} ({Point.X:0.###}, {Point.Y:0.###}, {Point.Z:0.###})");
                    }


                    Console.WriteLine($"  GUID: {area.GUID}");
                }


                Console.WriteLine("\nData extraction completed successfully.");

                // Display the Joints

                Console.WriteLine("\n===== Joints =====\n");
                for (int i = 0; i < jointCount; i++)
                {
                    var joint = jointObjectList[i];
                    Console.WriteLine($"Joint {i + 1}: {joint.Name}");
                    Console.WriteLine($"  Coordinates: ({joint.X:0.###}, {joint.Y:0.###}, {joint.Z:0.###})");
                    Console.WriteLine($"  Constraints (U1, U2, U3, R1, R2, R3): {string.Join(", ", joint.Constraint.Select(c => c ? "Yes" : "No"))}");
                    Console.WriteLine($"  Support Type: {joint.SupportType}");
                    Console.WriteLine($"  GUID: {joint.GUID}");
                }



                // Display the Load Cases
                Console.WriteLine("\n===== Load Cases =====\n");
                for (int i = 0; i < loadCasesInfos.Count; i++)
                {
                    var loadCase = loadCasesInfos[i];
                    Console.WriteLine($"Load Case {i + 1}: {loadCase.Name}");
                    Console.WriteLine($"  Type: {loadCase.LoadCaseType}");
                    Console.WriteLine($"  Subtype: {loadCase.SubTypeName}");
                    Console.WriteLine($"  Number of Loads: {loadCase.numberOfLoads}");

                    if (loadCase.LoadCaseType == eLoadCaseType.NonlinearHistory)
                    {
                        for (int j = 0; j < loadCase.numberOfLoads; j++)
                        {
                            Console.WriteLine($"    Load {j + 1}: Type: {loadCase.Loadtype[j]}, Name: {loadCase.LoadName[j]}, Function: {loadCase.function[j]}, Scale Factor: {loadCase.scaleFactors[j]}");
                        }
                    }
                }

                // Display the User Functions
                Console.WriteLine("\n===== User Functions =====\n");
                for (int i = 0; i < functions.Count; i++)
                {
                    var function = functions[i];
                    Console.WriteLine($"Function {i + 1}: {function.Name}");
                    Console.WriteLine($"  Type: {function.FuncTypeName}, Add Type: {function.AddTypeName}");
                }

                #endregion

                Console.WriteLine("\nData extraction completed successfully.");

                Console.WriteLine("\nAll data extraction completed successfully.");

                Console.WriteLine("\n--- ETABS Model Data Extraction Completed ---");

                #endregion

                #region Saving Document and Running Analysis


                string folderPath = @"C:\ETABS_API_TEST";
                string folderDir = folderPath;
                string folderNameWithoutExt = Path.GetFileNameWithoutExtension(folderPath);


                Directory.CreateDirectory(folderPath);
                Console.WriteLine($"\nCreated folder: {folderPath}");


                sapModel.View.RefreshWindow();

                // Saving the Model
                sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD");

                e2tFolder = folderDir;
                e2tFilePath = Path.Combine(folderPath, "ETABs_Plugin.$et");

                Console.WriteLine("\nModel saved successfully in the new folder.");

                // Analysing the Model

                sapModel.Analyze.SetRunCaseFlag("Dead", true);
                sapModel.Analyze.SetRunCaseFlag("IDA", false);
                sapModel.Analyze.SetRunCaseFlag("Modal",false);
                sapModel.Analyze.SetRunCaseFlag("PUSHOVER", false);

                Console.WriteLine("\n------------------------------------" +
                    "\nNow Running Analysis.... for Dead Load\n" +
                    "------------------------------------\n");
                
                sapModel.Analyze.RunAnalysis();

                #endregion

                #region Getting the Tables

                // Get results tables
                cDatabaseTables myTables = sapModel.DatabaseTables;

                // Define the name of the table to retrieve
                string tableName = "Joint Displacements";

                // Initialize variables to hold the table data
                int tableVersion = 0;
                string[] fieldKeyList = null;
                int numRecords = 0;
                string[] tableData = null;
                string[] tableDataHeaders = null;

                // Select the load combination for which you want to retrieve results
                sapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();
                sapModel.Results.Setup.SetComboSelectedForOutput("Dead");

                // Retrive the table for display
                myTables.GetTableForDisplayArray(tableName, ref fieldKeyList, "All", ref tableVersion, ref tableDataHeaders, ref numRecords, ref tableData);

                Console.WriteLine($"\n--- Successfully retrieved table: {tableName} for Combo: IDA ---");

                #endregion

                #region Saving Data to Variables

                var Dataall = new List<string[]>();
                // Convert the flat data array into a list of string arrays for easier processing
                Dataall.Add(tableDataHeaders); // Add headers as the first row

                // Loop through the flat tableData array and create a list of string arrays
                for (int i = 0; i < tableData.Length; i += tableDataHeaders.Length)
                {
                    string[] dat = new string[tableDataHeaders.Length];
                    for (int j = i; j < (i + tableDataHeaders.Length); j++)
                    {
                        dat[j - i] = tableData[j];
                    }
                    Dataall.Add(dat);
                }

                // Show the List Dataall on the Console
                Console.WriteLine("\nData retrieved from the table:");
                foreach (var row in Dataall)
                {
                    Console.WriteLine(string.Join("| ", row));
                }

                #endregion

                #region Connecting to MATLAB

                #region Opening MATLAB

                // Create an instance of the MATLAB COM object

                matlab = new MLApp.MLApp();


                // Optional: Make the MATLAB application window visible
                matlab.Visible = 0;
                matlab.Execute("clear");

                Console.WriteLine("\nConnected to MATLAB successfully.");

                #endregion

                #region Sending the Table Data to MATLAB
                // Convert the List<string[]> to a 2D object array for MATLAB
                // The 'Dataall' variable (List<string[]>) from ETABS has mixed text and numbers.
                // The COM interface's PutWorkspaceData method can handle a simple object array.
                int numRows = Dataall.Count;
                int numCols = Dataall[0].Length;
                object[,] matlabCellData = new object[numRows, numCols];

                for (int i = 0; i < numRows; i++)
                {
                    for (int j = 0; j < numCols; j++)
                    {
                        // Simply assign the string value. MATLAB will interpret it.
                        matlabCellData[i, j] = Dataall[i][j];
                    }
                }

                // Sending the storyList to MATLAB
                object[,] storyCellData = new object[storyList.Count, 3];
                for (int i = 0;
                    i < storyList.Count;
                    i++)
                {
                    string tempname = storyList[i].Name;
                    string tempelevation = storyList[i].Elevation.ToString();
                    string tempheight = storyList[i].Height.ToString();

                    storyCellData[i, 0] = tempname;
                    storyCellData[i, 1] = tempelevation;
                    storyCellData[i, 2] = tempheight;

                }

                // Sending the storyList to MATLAB
                matlab.PutWorkspaceData("StoryData", "base", storyCellData);


                // Sending the data to MATLAB
                matlab.PutWorkspaceData("ETABSData", "base", matlabCellData);
                //matlab.PutWorkspaceData("StoryHeights",,)

                Console.WriteLine("\nData sent to MATLAB successfully.");
                #endregion

                #region Processing
                string folderPath1 = @"C:\ETABS_IDA_Results";

                Directory.CreateDirectory(folderPath1);

                #region Initials


                int SNo = 0;
                int Sub_SNo = 0;
                string csvFilePath = Path.Combine(folderPath1, "ETABS_Data.csv");
                string csvHeader = "SNo,Sub_SNo,IterationName,Story,BayX,BayY,BayAspectRatio,ColumnSize,BeamSize,SlabThickness,ConcreteStrength,ConcreteModulus,Column_Fy,Beam_Fy,SiteClass, G, v, Friction Angle, Unit Weight, Shear Wave Velocity, Spring Stifness Horizontal, Spring Stifness Vertical, Spring Stifness Rocking,FunctionName,ScaleFactor,PGA_g,Sa(T1_5),MaxIDR_g,Time_Period,Frequency,DS1_PO,DS2_PO,DS3_PO,DS4_PO,DS5_PO,DS1_mean,DS1_sd,DS2_mean,DS2_sd,DS3_mean,DS3_sd,DS4_mean,DS4_sd,DS5_mean,DS5_sd";
                // Write the header to the CSV file

                if (File.Exists(csvFilePath) == false)
                {
                    File.WriteAllText(csvFilePath, csvHeader + Environment.NewLine); // This will create the file if it doesn't exist
                }

                // reading from text file 
                string textFilePath = Path.Combine(folderPath1, "ETABS_Data.txt");

                // Reading Line 1 to find storey_starter Variable

                string[] lines = File.ReadAllLines(textFilePath);
                int storey_starter = int.Parse(lines[0]);

                // Show the storey starter variable in the console
                Console.WriteLine($"Storey Starter: {storey_starter}"); // Start from the first story


                // Reading Line 2 to find g_x_starter variable              
                int g_x_starter = int.Parse(lines[1]); // Start from the first bay in x direction

                //Show the g_x_starter variable in the console
                Console.WriteLine($"g_x_starter: {g_x_starter}"); // Start from the first bay in x direction


                int g_y_starter = int.Parse(lines[2]); // Start from the first bay in y direction
                // Show the g_y_starter variable in the console
                Console.WriteLine($"g_y_starter: {g_y_starter}"); // Start from the first bay in y direction

                int iteration = int.Parse(lines[3]);
                // Show the iteration variable in the console
                Console.WriteLine($"Iteration: {lines[3]}"); // Start from the first iteration

                SNo = int.Parse(lines[3]);

                bool firstTimeRun = true;

                int functionCounter = int.Parse(lines[4]);
                //double functionpoint = double.Parse(lines[5]);

                

                #endregion

                Random random = new Random();

                double max_function_value = 0;
                functions.RemoveAll(func => func.AddTypeName != "From file"); // Remove any empty function names

                foreach (var func in functions)
                {
                    if (func.maxValue > max_function_value)
                    {
                        max_function_value = func.maxValue;
                    }
                }

               

                for (int storey = storey_starter; storey < storyList.Count; storey++) // Going over and adding stories
                {
                    for (int g_x = g_x_starter; g_x < grid.NumberOfBaysX; g_x++) // Going over and adding frames in x direction
                    {
                        for (int g_y = g_y_starter; g_y < grid.NumberOfBaysY; g_y++) // Going over and adding frames in y direction
                        {
                            if (firstTimeRun)
                            {
                                functionCounter = int.Parse(lines[4]);
                                //functionpoint = double.Parse(lines[5]);
                            }
                            else
                            {
                                functionCounter = 0;
                                //functionpoint = 0;
                            }
                            

                            // Update the ETABS_Data.txt file
                            string etabsDataFilePath = Path.Combine(folderPath1, "ETABS_Data.txt");

                            // Clear the File of all contents
                            File.WriteAllText(etabsDataFilePath, string.Empty);


                            double x1 = grid.XCoordinates[g_x];
                            double y1 = grid.YCoordinates[g_y];
                            double z1 = storyList[storey - 1].Elevation; // Get the elevation of the current story
                            double x2 = grid.XCoordinates[g_x + 1];
                            double y2 = grid.YCoordinates[g_y + 1];
                            double z2 = storyList[storey].Elevation; // Keep the same elevation for the end point

                            sapModel.SetModelIsLocked(false); // Unlock the model for modifications

                            // removing all frameobjects from the frameobjectlist and repopulating it again from the actual model
                            frameObjectList.Clear();

                            Importing_Frame_Objects(sapModel, frameSectionList, frameObjectList);
                            string stry = null;

                            for (int i = 0; i < frameObjectList.Count; i++)
                            {
                                string lbl = null;

                                sapModel.FrameObj.GetLabelFromName(frameObjectList[i].Name, ref lbl, ref stry); // Get the GUID of the frame object

                                if (lbl == null)
                                {
                                    frameObjectList.RemoveAt(i);
                                }

                                frameObjectList[i].Label = lbl;
                            }

                            #region Adding Elements
                            var materialsectioonListConcreteRandom = materialList
                                .Where(s => s.Name.ToUpper().StartsWith("C"))
                                .ToList();
                            MaterialInfo materialRandom = materialsectioonListConcreteRandom[random.Next(0, materialsectioonListConcreteRandom.Count)]; // Get a random material from the list



                            #region Columns

                            // Adding 4 Vertical Columns

                            // Column 1 name
                            string name1 = $"Column_{g_x}_{g_y}_{storey}";
                            string name2 = $"Column_{g_x + 1}_{g_y}_{storey}";
                            string name3 = $"Column_{g_x}_{g_y + 1}_{storey}";
                            string name4 = $"Column_{g_x + 1}_{g_y + 1}_{storey}";

                            // Look for all the frame section list names that have the word "column" in them and choose a random one amongst them
                            var columnSectionsList = frameSectionList
                                .Where(s => s.Name.ToLower().Contains("column"))
                                .ToList();

                            FrameSectionInfo colummnSectionRandom = columnSectionsList[random.Next(0, columnSectionsList.Count)]; // Get a random column section from the list

                            colummnSectionRandom.Material = materialRandom; // Assign the random material to the column section

                            sapModel.PropFrame.SetMaterial(colummnSectionRandom.Name, materialRandom.Name); // Set the material for the column section in SAP2000

                            // Logic to determine properties based on the suffix
                            if (colummnSectionRandom.Name.IndexOf("Legacy", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                colummnSectionRandom.Fy = 280;
                            }
                            else if (colummnSectionRandom.Name.IndexOf("Std", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Category 2: Standard (Grade 420)
                                //rebarMatName = "Rebar_Gr420_A615";
                                //fy_Main = 420;
                                colummnSectionRandom.Fy = 420;
                            }
                            else if (colummnSectionRandom.Name.IndexOf("HighStr", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Category 3: High Strength (Grade 550)
                                //rebarMatName = "Rebar_Gr550_HighStr";
                                //fy_Main = 550;
                                colummnSectionRandom.Fy = 550;
                            }
                            else
                            {
                                // Fallback/Error handling
                                //rebarMatName = "Rebar_Gr420_A615"; // Default to standard if no tag found
                                Console.WriteLine("Warning: No category tag found in " + colummnSectionRandom);
                            }


                            // Update the section name in the frameSectionList
                            string columnSection = colummnSectionRandom.Name; // Assuming a predefined column section name



                            // Updating the framesection of all column frameobjects

                            foreach (var frameObject in frameObjectList)
                            {
                                if (frameObject.SectionName.Name.ToLower().Contains("column"))
                                {
                                    frameObject.SectionName = colummnSectionRandom;
                                    sapModel.FrameObj.SetSection(frameObject.Name, columnSection);
                                    sapModel.FrameObj.SetSpringAssignment(frameObject.Name, columnSection);
                                }
                            }

                            

                            sapModel.FrameObj.AddByCoord(x1, y1, z1, x1, y1, z2, ref name1, columnSection, "1", "Global");
                            string lblname1 = null;
                            sapModel.FrameObj.GetLabelFromName(name1, ref lblname1, ref stry); // Get the GUID of the frame object
                            sapModel.View.RefreshView();

                            sapModel.FrameObj.AddByCoord(x2, y1, z1, x2, y1, z2, ref name2, columnSection, "1", "Global");
                            string lblname2 = null;
                            sapModel.FrameObj.GetLabelFromName(name2, ref lblname2, ref stry); // Get the GUID of the frame object
                            sapModel.View.RefreshView();

                            sapModel.FrameObj.AddByCoord(x1, y2, z1, x1, y2, z2, ref name3, columnSection, "1", "Global");
                            string lblname3 = null;
                            sapModel.FrameObj.GetLabelFromName(name3, ref lblname3, ref stry); // Get the GUID of the frame object
                            sapModel.View.RefreshView();

                            sapModel.FrameObj.AddByCoord(x2, y2, z1, x2, y2, z2, ref name4, columnSection, "1", "Global");
                            string lblname4 = null;
                            sapModel.FrameObj.GetLabelFromName(name4, ref lblname4, ref stry); // Get the GUID of the frame object
                            sapModel.View.RefreshView();

                            Console.WriteLine($"Added columns: {name1}, {name2}, {name3}, {name4} at story {storyList[storey].Name} with Column Section:{columnSection}");
                            // Add the columns to the frameObjectList

                            // Adding the columns to the frameObjectList
                            Console.WriteLine(
                                $"Adding columns: {name1}, {name2}, {name3}, {name4} at story {storyList[storey].Name}");

                            frameObjectList.Add(new FrameObject
                            {
                                Name = name1,
                                sapModel = sapModel,
                                Label = lblname1,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == columnSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Vertical Column",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name1, name2 },
                            });
                            Console.WriteLine(
                                $"Added column {name1} with section {columnSection} at story {storyList[storey].Name}");


                            //sapModel.FrameObj.GetLabelFromName(name2, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name2,
                                Label = lblname2,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == columnSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Vertical Column",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name2, name4 },
                            });
                            Console.WriteLine(
                                $"Added column {name2} with section {columnSection} at story {storyList[storey].Name}");


                            //sapModel.FrameObj.GetLabelFromName(name3, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name3,
                                Label = lblname3,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == columnSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Vertical Column",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name3, name4 },
                            });
                            Console.WriteLine(
                                $"Added column {name3} with section {columnSection} at story {storyList[storey].Name}");


                            //sapModel.FrameObj.GetLabelFromName(name4, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name4,
                                Label = lblname4,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == columnSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Vertical Column",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name4, name2 },
                            });
                            Console.WriteLine(
                                $"Added column {name4} with section {columnSection} at story {storyList[storey].Name}");


                            #endregion

                            #region Beams
                            // Look for all the frame section list names that have the word "column" in them and choose a random one amongst them
                            var breamsectionList = frameSectionList
                                .Where(s => s.Name.ToLower().Contains("beam"))
                                .ToList();


                            FrameSectionInfo beamSectionRandom = breamsectionList[random.Next(0, breamsectionList.Count)]; // Get a random column section from the list
                            beamSectionRandom.Material = materialRandom; // Assign the random material to the beam section
                            string beamSection = beamSectionRandom.Name; // Assuming a predefined column section name

                            // Logic to determine properties based on the suffix
                            if (beamSectionRandom.Name.IndexOf("Legacy", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                beamSectionRandom.Fy = 280;
                            }
                            else if (beamSectionRandom.Name.IndexOf("Std", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Category 2: Standard (Grade 420)
                                //rebarMatName = "Rebar_Gr420_A615";
                                //fy_Main = 420;
                                beamSectionRandom.Fy = 420;
                            }
                            else if (beamSectionRandom.Name.IndexOf("HighStr", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Category 3: High Strength (Grade 550)
                                //rebarMatName = "Rebar_Gr550_HighStr";
                                //fy_Main = 550;
                                beamSectionRandom.Fy = 550;
                            }
                            else
                            {
                                // Fallback/Error handling
                                //rebarMatName = "Rebar_Gr420_A615"; // Default to standard if no tag found
                                Console.WriteLine("Warning: No category tag found in " + beamSectionRandom);
                            }

                            sapModel.PropFrame.SetMaterial(beamSectionRandom.Name, materialRandom.Name); // Set the material for the column section in SAP2000
                            // Updating the framesection of all beam frameobjects

                            foreach (var frameObject in frameObjectList)
                            {
                                if (frameObject.SectionName.Name.ToLower().Contains("beam"))
                                {
                                    frameObject.SectionName = beamSectionRandom;
                                    sapModel.FrameObj.SetSection(frameObject.Name, beamSection);
                                }
                            }


                            // Adding 4 Horizontal Bottom Beams
                            if (z1 > 0)
                            {

                                string name5 = $"Beam_{g_x}_{g_y}_{storey}_Bottom";
                                string name6 = $"Beam_{g_x + 1}_{g_y}_{storey}_Bottom";
                                string name7 = $"Beam_{g_x}_{g_y + 1}_{storey}_Bottom";
                                string name8 = $"Beam_{g_x + 1}_{g_y + 1}_{storey}_Bottom";
                                sapModel.FrameObj.AddByCoord(x1, y1, z1, x2, y1, z1, ref name5, beamSection);
                                string lblname5 = null;
                                sapModel.FrameObj.GetLabelFromName(name5, ref lblname5, ref stry); // Get the GUID of the frame object

                                sapModel.View.RefreshView();
                                sapModel.FrameObj.AddByCoord(x2, y1, z1, x2, y2, z1, ref name6, beamSection);
                                string lblname6 = null;
                                sapModel.FrameObj.GetLabelFromName(name6, ref lblname6, ref stry); // Get the GUID of the frame object

                                sapModel.View.RefreshView();
                                sapModel.FrameObj.AddByCoord(x1, y2, z1, x2, y2, z1, ref name7, beamSection);
                                string lblname7 = null;
                                sapModel.FrameObj.GetLabelFromName(name6, ref lblname7, ref stry); // Get the GUID of the frame object

                                sapModel.View.RefreshView();
                                sapModel.FrameObj.AddByCoord(x1, y1, z1, x1, y2, z1, ref name8, beamSection);
                                string lblname8 = null;
                                sapModel.FrameObj.GetLabelFromName(name6, ref lblname8, ref stry); // Get the GUID of the frame object


                                Console.WriteLine($"Added beams: {name5}, {name6}, {name7}, {name8} at story {storyList[storey].Name} with Section:{beamSection}");
                                // Add the beams to the frameObjectList

                                //sapModel.FrameObj.GetLabelFromName(name5, ref label, ref story); // Get the GUID of the frame object
                                frameObjectList.Add(new FrameObject
                                {
                                    Name = name5,
                                    Label = lblname5,
                                    sapModel = sapModel,
                                    SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                    MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                    GUID = Guid.NewGuid().ToString(),
                                    Notes = "Horizontal Beam",
                                    Story = storyList[storey].Name,
                                    PointNames = new string[] { name5, name6 },
                                });
                                Console.WriteLine(
                                    $"Added beam {name5} with section {beamSection} at story {storyList[storey].Name}");

                                //sapModel.FrameObj.GetLabelFromName(name6, ref label, ref story); // Get the GUID of the frame object
                                frameObjectList.Add(new FrameObject
                                {
                                    Name = name6,
                                    Label = lblname6,
                                    sapModel = sapModel,
                                    SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                    MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                    GUID = Guid.NewGuid().ToString(),
                                    Notes = "Horizontal Beam",
                                    Story = storyList[storey].Name,
                                    PointNames = new string[] { name6, name8 },
                                });
                                Console.WriteLine(
                                    $"Added beam {name6} with section {beamSection} at story {storyList[storey].Name}");

                                //sapModel.FrameObj.GetLabelFromName(name7, ref label, ref story); // Get the GUID of the frame object
                                frameObjectList.Add(new FrameObject
                                {
                                    Name = name7,
                                    Label = lblname7,
                                    sapModel = sapModel,
                                    SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                    MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                    GUID = Guid.NewGuid().ToString(),
                                    Notes = "Horizontal Beam",
                                    Story = storyList[storey].Name,
                                    PointNames = new string[] { name7, name8 },
                                });
                                Console.WriteLine(
                                    $"Added beam {name7} with section {beamSection} at story {storyList[storey].Name}");

                                //sapModel.FrameObj.GetLabelFromName(name8, ref label, ref story); // Get the GUID of the frame object
                                frameObjectList.Add(new FrameObject
                                {
                                    Name = name8,
                                    Label = lblname8,
                                    sapModel = sapModel,
                                    SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                    MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                    GUID = Guid.NewGuid().ToString(),
                                    Notes = "Horizontal Beam",
                                    Story = storyList[storey].Name,
                                    PointNames = new string[] { name8, name5 },
                                });
                                Console.WriteLine(
                                    $"Added beam {name8} with section {beamSection} at story {storyList[storey].Name}");
                            }



                            // Adding 4 Horizontal Top Beams
                            string name9 = $"Beam_{g_x}_{g_y}_{storey}_Top";
                            string name10 = $"Beam_{g_x + 1}_{g_y}_{storey}_Top";
                            string name11 = $"Beam_{g_x}_{g_y + 1}_{storey}_Top";
                            string name12 = $"Beam_{g_x + 1}_{g_y + 1}_{storey}_Top";
                            sapModel.FrameObj.AddByCoord(x1, y1, z2, x2, y1, z2, ref name9, beamSection);
                            string lblname9 = null;
                            sapModel.FrameObj.GetLabelFromName(name9, ref lblname9, ref stry); // Get the GUID of the frame object

                            sapModel.View.RefreshView();
                            sapModel.FrameObj.AddByCoord(x2, y1, z2, x2, y2, z2, ref name10, beamSection);
                            string lblname10 = null;
                            sapModel.FrameObj.GetLabelFromName(name10, ref lblname10, ref stry); // Get the GUID of the frame object

                            sapModel.View.RefreshView();
                            sapModel.FrameObj.AddByCoord(x1, y2, z2, x2, y2, z2, ref name11, beamSection);
                            string lblname11 = null;
                            sapModel.FrameObj.GetLabelFromName(name10, ref lblname11, ref stry); // Get the GUID of the frame object

                            sapModel.View.RefreshView();
                            sapModel.FrameObj.AddByCoord(x1, y1, z2, x1, y2, z2, ref name12, beamSection);
                            string lblname12 = null;
                            sapModel.FrameObj.GetLabelFromName(name12, ref lblname12, ref stry); // Get the GUID of the frame object

                            sapModel.View.RefreshView();
                            Console.WriteLine($"Added beams: {name9}, {name10}, {name11}, {name12} at story {storyList[storey].Name}");
                            // Add the beams to the frameObjectList

                            //sapModel.FrameObj.GetLabelFromName(name9, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name9,
                                Label = lblname9,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Horizontal Beam",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name9, name10 },
                            });
                            Console.WriteLine(
                                $"Added beam {name9} with section {beamSection} at story {storyList[storey].Name}");

                            //sapModel.FrameObj.GetLabelFromName(name10, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name10,
                                Label = lblname10,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Horizontal Beam",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name10, name12 },
                            });
                            Console.WriteLine(
                                $"Added beam {name10} with section {beamSection} at story {storyList[storey].Name}");

                            //sapModel.FrameObj.GetLabelFromName(name11, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name11,
                                Label = lblname11,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Horizontal Beam",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name11, name12 },
                            });
                            Console.WriteLine(
                                $"Added beam {name11} with section {beamSection} at story {storyList[storey].Name}");

                            //sapModel.FrameObj.GetLabelFromName(name12, ref label, ref story); // Get the GUID of the frame object
                            frameObjectList.Add(new FrameObject
                            {
                                Name = name12,
                                Label = lblname12,
                                sapModel = sapModel,
                                SectionName = frameSectionList.FirstOrDefault(s => s.Name == beamSection),
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Horizontal Beam",
                                Story = storyList[storey].Name,
                                PointNames = new string[] { name12, name9 },
                            });
                            Console.WriteLine(
                                $"Added beam {name12} with section {beamSection} at story {storyList[storey].Name}");

                            #endregion

                            #region Adding Slabs 
                            var slabSectionList = areaSectionList
                               .Where(s => s.Name.ToLower().Contains("slab"))
                               .ToList();

                            AreaSectionInfo areasectionRandom = slabSectionList[random.Next(0, slabSectionList.Count)]; // Get a random area section from the list
                            areasectionRandom.MaterialName = materialRandom; // Assign the random material to the slab section
                            // Updating the framesection of all column frameobjects
                            sapModel.AreaObj.SetMaterialOverwrite(colummnSectionRandom.Name, materialRandom.Name); // Set the material for the column section in SAP2000

                            foreach (var area in areaObjectList)
                            {
                                if (area.AreaType.Name.ToLower().Contains("slab"))
                                {
                                    area.AreaType = areasectionRandom;
                                    sapModel.AreaObj.SetProperty(area.Name, areasectionRandom.Name);
                                }
                            }

                            string slabSection = areasectionRandom.Name; // Assuming a predefined slab section name
                            int numberOfPoints = 4; // Number of points for the slab
                            string name13 = $"Slab_{g_x}_{g_y}_{storey}_Top";
                            double[] X_coordinates = { x1, x2, x2, x1 };
                            double[] Y_coordinates = { y1, y1, y2, y2 };
                            double[] Z_coordinates = { z2, z2, z2, z2 }; // All points at the top elevation of the story

                            // Adding Bottom Slab
                            if (storey > 1)
                            {

                                string name14 = $"Slab_{g_x}_{g_y}_{storey}_Bottom";
                                double[] Z_coordinates_bottom = { z1, z1, z1, z1 }; // All points at the bottom elevation of the story
                                Console.WriteLine(
                                    $"Adding slab: {name14} with section {slabSection} at story {storyList[storey].Name}");
                                string slabNameBottom = $"Slab_{g_x}_{g_y}_{storey}_Bottom";
                                sapModel.AreaObj.AddByCoord(numberOfPoints, ref X_coordinates, ref Y_coordinates, ref Z_coordinates_bottom, ref slabNameBottom, slabSection);
                                sapModel.View.RefreshView();
                                Console.WriteLine($"Added slab: {name14} at story {storyList[storey].Name}");
                                // Add the slab to the areaObjectList
                                areaObjectList.Add(new AreaObject
                                {
                                    Name = name14,
                                    sapModel = sapModel,
                                    AreaType = areaSectionList.FirstOrDefault(a => a.Name == slabSection),
                                    ShellType = eShellType.ShellThin, // Assuming a thin shell type for slabs
                                    SlabType = eSlabType.Slab, // Assuming a flat plate slab type
                                    MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                    Thickness = 0.2, // Assuming a thickness of 200 mm
                                    PointNames = new string[] { name14 },
                                    GUID = Guid.NewGuid().ToString(),
                                    Notes = "Bottom Slab",
                                });

                            }

                            // Adding Top Slab
                            Console.WriteLine(
                                    $"Adding slab: {name1} with section {slabSection} at story {storyList[storey].Name}");
                            string slabName = $"Slab_{g_x}_{g_y}_{storey}_Top";
                            sapModel.AreaObj.AddByCoord(numberOfPoints, ref X_coordinates, ref Y_coordinates, ref Z_coordinates, ref slabName, slabSection);
                            sapModel.View.RefreshView();
                            Console.WriteLine($"Added slab: {name13} at story {storyList[storey].Name}");
                            // Add the slab to the areaObjectList
                            areaObjectList.Add(new AreaObject
                            {
                                Name = name13,
                                sapModel = sapModel,
                                AreaType = areaSectionList.FirstOrDefault(a => a.Name == slabSection),
                                ShellType = eShellType.ShellThin, // Assuming a thin shell type for slabs
                                SlabType = eSlabType.Slab, // Assuming a flat plate slab type
                                MaterialName = materialList.FirstOrDefault(m => m.Name == "Concrete"),
                                Thickness = 0.2, // Assuming a thickness of 200 mm
                                PointNames = new string[] { name13 },
                                GUID = Guid.NewGuid().ToString(),
                                Notes = "Top Slab",
                            });

                            #endregion

                            #region Applying Sprins

                            // Assuming you have 'sapModel' and 'base_springs' defined already:
                            // 2. Random Selection
                            selectedSpring = base_springs[0];

                            var selectedProfile = new SoilProfile();

                            // Assuming you have 'sapModel' and 'base_springs' defined already:

                            SoilProfile.GetFoundationDimensions(ref sapModel, "Base", ref Length, ref Bredth);

                            SoilProfile.CalculateRandomSoilSprings(ref sapModel, soil_profiles, Length, Bredth, ref selectedSpring, ref selectedProfile);

                            // Assuming you have 'sapModel' and 'base_springs' defined already:

                            SpringInfo.AssignRandomSpringToStory(ref sapModel, selectedSpring, "Base");

                            #endregion

                            #endregion

                            #region Applying Live Loads to the structure

                            sapModel.SelectObj.All(true);
                            sapModel.View.RefreshView();

                            int numberOfAreaObjects = 0;
                            string[] areaObjectNames_ForLoad = null;

                            sapModel.AreaObj.GetNameList(ref numberOfAreaObjects, ref areaObjectNames_ForLoad);

                            sapModel.View.RefreshView();

                            foreach (string areaObject in areaObjectNames_ForLoad)
                            {
                                sapModel.AreaObj.SetSelected(areaObject, true);
                                sapModel.View.RefreshView();
                                // Apply the load to each slab section
                                sapModel.AreaObj.SetLoadUniform(areaObject, "Live", -0.002394, 6);
                            }

                            Console.WriteLine("\nLive Load apllied to all slabs");



                            #endregion

                            #region Updating the ETABS Data File

                            File.WriteAllText(etabsDataFilePath, string.Empty);
                            // Update the ETABS_Data.txt file with the current storey, g_x, and g_y
                            using (StreamWriter sw = new StreamWriter(etabsDataFilePath, true))
                            {
                                sw.WriteLine($"{storey}");
                                sw.WriteLine($"{g_x}");
                                sw.WriteLine($"{g_y}");
                                sw.WriteLine($"{iteration}");
                                sw.WriteLine($"{functionCounter}");
                                //sw.WriteLine($"{functionpoint}");
                            }

                            #endregion

                            #region Saving the ETABs Window and Rewriting the Frame List


                            // --- Start: Code to replace line 1809 ---                         


                            frameObjectList.Clear();

                            Importing_Frame_Objects(sapModel, frameSectionList, frameObjectList);

                            for (int i = 0; i < frameObjectList.Count; i++)
                            {
                                string lbl = null;

                                sapModel.FrameObj.GetLabelFromName(frameObjectList[i].Name, ref lbl, ref stry); // Get the GUID of the frame object

                                if (lbl == null)
                                {
                                    frameObjectList.RemoveAt(i);
                                }

                                frameObjectList[i].Label = lbl;
                            }

                            Directory.CreateDirectory(folderPath);
                            Console.WriteLine($"\nCreated folder: {folderPath}");

                            // Save the model in the a seperate folder for backup
                            string backupFolderPath = Path.Combine(folderPath, "Backup");
                            Directory.CreateDirectory(backupFolderPath);
                            Console.WriteLine($"\nCreated backup folder: {backupFolderPath}");
                            // Save the model in the backup folder
                            sapModel.File.Save(backupFolderPath + '\\' + "ETABs_Plugin_Backup.EBD");
                            sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD");
                            Console.WriteLine("\nModel saved successfully in the backup folder.");

                            e2tFilePath = backupFolderPath + '\\' + "ETABs_Plugin_Backup.$et";

                            Parsing.RemoveFrameHingeAssignments(e2tFilePath); // Remove all hinge assignments from the ETABs file

                            var hinges_column = hinges
                                        .Where(h => h.Name.ToLower().Contains("column"))
                                        .ToList();
                            var hinges_beam = hinges
                                        .Where(h => h.Name.ToLower().Contains("beam"))
                                        .ToList();

                            foreach (var frameObject in frameObjectList)
                            {

                                if (frameObject.SectionName.Name.ToLower().Contains("column"))
                                {
                                    string newLine_a = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEDISTRTYPE \"User Defined\"  ";
                                    string newLine_b = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEPROP \"{hinges_column[0].Name}\"  LOCATIONTYPE \"RelDist\"  RDISTANCE 0.05 LENGTHOWTYPE \"None\"";
                                    string newLine_c = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEPROP \"{hinges_column[0].Name}\"  LOCATIONTYPE \"RelDist\"  RDISTANCE 0.95 LENGTHOWTYPE \"None\"";

                                    string newLine = newLine_a + "\n" + newLine_b + "\n" + newLine_c;

                                    Parsing.InsertLineBeforeAreaAssigns(e2tFilePath, newLine);

                                }
                                else if (frameObject.SectionName.Name.ToLower().Contains("beam"))
                                {
                                    // seperating hinges having the word "column" in them into another lise


                                    string newLine_a = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEDISTRTYPE \"User Defined\"  ";
                                    string newLine_b = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEPROP \"{hinges_beam[0].Name}\"  LOCATIONTYPE \"RelDist\"  RDISTANCE 0.05 LENGTHOWTYPE \"None\"";
                                    string newLine_c = $"  HINGEASSIGN \"{frameObject.Label}\"  \"{frameObject.Story}\"  HINGEPROP \"{hinges_beam[0].Name}\"  LOCATIONTYPE \"RelDist\"  RDISTANCE 0.95 LENGTHOWTYPE \"None\"";

                                    string newLine = newLine_a + "\n" + newLine_b + "\n" + newLine_c;

                                    Parsing.InsertLineBeforeAreaAssigns(e2tFilePath, newLine);
                                }
                                else
                                {
                                    continue; // Skip if the section name does not contain "column" or "beam"
                                }
                            }


                            #endregion

                            #region Fundamental Time Period

                            #region Opening File Again
                            
                            
                            etabsObject.ApplicationExit(true);

                            etabsObject = helper.CreateObjectProgID("CSI.ETABS.API.ETABSObject");
                            etabsObject.ApplicationStart();


                            sapModel = etabsObject.SapModel;


                            sapModel.File.ImportFile(e2tFilePath, eFileTypeIO.TextFile, 1); // Reopen the ETABs file to refresh the model
                            sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD"); // Save the model in the new folder



                            #endregion

                            #region Running Analysis

                            sapModel.SetModelIsLocked(false); // Unlock the model to allow changes

                            sapModel.SetPresentUnits(eUnits.N_mm_C);

                            sapModel.Analyze.SetRunCaseFlag("PUSHOVER", false); // Disable Pushover Analysis
                            sapModel.Analyze.SetRunCaseFlag("IDA", false); // Disable Pushover Analysis
                            sapModel.Analyze.SetRunCaseFlag("Modal", true); // Enable Model


                            Console.WriteLine("\n------------------------------------" +
                                         "\nNow Running Analysis.... for Model\n" +
                                            "------------------------------------\n");


                            sapModel.Analyze.RunAnalysis();

                            #endregion

                            #region Getting Data


                            cDatabaseTables myTables_Fund_Time_Period = sapModel.DatabaseTables;

                            // Define the name of the table to retrieve
                            string tableName_local_FT = "Modal Periods And Frequencies";

                            // Initialize variables to hold the table data
                            int tableVersion_local_FT = 0;
                            string[] fieldKeyList_local_FT = null;
                            int numRecords_local_FT = 0;
                            string[] tableData_local_FT = null;
                            string[] tableDataHeaders_local_FT = null;

                            // Select the load combination for which you want to retrieve results
                            sapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();
                            //sapModel.Results.Setup.SetComboSelectedForOutput("1.4D");

                            // Retrive the table for display
                            string[] loadCases_FT = { "Modal" }; // Set the load case to PUSHOVER


                            // Get results tables


                            myTables_Fund_Time_Period.SetOutputOptionsForDisplay(true, 0, 0, 0, false, 0, 0, false, 0, 0, 3, 2, 3, 3, 2);
                            myTables_Fund_Time_Period.SetLoadCasesSelectedForDisplay(ref loadCases_FT); // Set the load case for display

                            myTables_Fund_Time_Period.GetTableForDisplayArray(tableName_local_FT, ref fieldKeyList_local_FT, "",
                                ref tableVersion_local_FT, ref tableDataHeaders_local_FT, ref numRecords_local_FT, ref tableData_local_FT);

                            Console.WriteLine($"\n--- Successfully retrieved table: {tableName_local_FT} for Combo: Model ---");

                            #endregion

                            #region Saving to MATLAB

                            Dataall.Clear();
                            Dataall.Add(tableDataHeaders_local_FT); // Add headers as the first row
                            for (int j = 0; j < tableData_local_FT.Length; j += tableDataHeaders_local_FT.Length)
                            {
                                string[] dat = new string[tableDataHeaders_local_FT.Length];
                                for (int k = j; k < (j + tableDataHeaders_local_FT.Length); k++)
                                {
                                    dat[k - j] = tableData_local_FT[k];
                                }
                                Dataall.Add(dat);
                            }


                            // Convert the updated Dataall to a MATLAB cell array
                            var matlabCellData_Time_Period = new object[Dataall.Count, Dataall[0].Length];
                            for (int j = 0; j < Dataall.Count; j++)
                            {
                                for (int k = 0; k < Dataall[0].Length; k++)
                                {
                                    matlabCellData_Time_Period[j, k] = Dataall[j][k];
                                }
                            }

                            // Sending the data to MATLAB
                            matlab.PutWorkspaceData("Fundamental_Time_Period_Table", "base", matlabCellData_Time_Period);

                            #endregion

                            #region Processing Data in MATLAB

                            // Chaning to BaseSheaer Table
                            matlab.Execute("Fundamental_Time_Period_Table = cell2table(Fundamental_Time_Period_Table(2:end,:));");

                            // Renaming the Variables
                            matlab.Execute("Fundamental_Time_Period_Table.Properties.VariableNames  = {'Case','Mode','Period','Frequency','CircFreq','Eigenvalue'};");

                            // Changing the Variables to Numeric
                            matlab.Execute("Fundamental_Time_Period_Table.Period = str2double(Fundamental_Time_Period_Table.Step);");
                            matlab.Execute("Fundamental_Time_Period_Table.Period = str2double(Fundamental_Time_Period_Table.Period);");
                            matlab.Execute("Fundamental_Time_Period= max(Fundamental_Time_Period_Table.Period);");

                            object Fundamental_Time_Period = null;
                            matlab.GetWorkspaceData("Fundamental_Time_Period", "base", out Fundamental_Time_Period);

                            double Frequency = 1 / double.Parse(Fundamental_Time_Period.ToString());

                            Console.WriteLine($"\nFundamental Time Period For this Run:{Fundamental_Time_Period}\n");

                            #endregion

                            #endregion

                            #region Finding IDR Values


                            #region Close Etabs and Open it again

                            etabsObject.ApplicationExit(true);

                            etabsObject = helper.CreateObjectProgID("CSI.ETABS.API.ETABSObject");
                            etabsObject.ApplicationStart();

                            sapModel = etabsObject.SapModel;

                            Parsing.UpdatePushoverLoadcase(e2tFilePath, storyElevations1[storey- 1] * 0.05, storyNames1[storey - 1]);

                            sapModel.File.ImportFile(e2tFilePath, eFileTypeIO.TextFile, 1); // Reopen the ETABs file to refresh the model
                            sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD"); // Save the model in the new folder

                            sapModel.SetModelIsLocked(false);

                            sapModel.SetPresentUnits(eUnits.N_mm_C);

                           // Find the ETABS window title dynamically. This is more robust than hardcoding it.
                            List<string> windowTitles = GetVisibleWindowTitles();
                            string etabsWindowTitle = windowTitles.FirstOrDefault(title => title.StartsWith("ETABS Ultimate"));

                            string iterationName = $"Iteration_{iteration}_Story_{storey}_Grid_X_{g_x}_Grid_Y_{g_y}";

                            if (!string.IsNullOrEmpty(etabsWindowTitle))
                            {
                                // Bring the ETABS window to the front and maximize it for a clean shot.
                                ActivateWindowByTitle(etabsWindowTitle);
                                sapModel.View.RefreshView();

                                Rectangle bounds = Screen.PrimaryScreen.Bounds;

                                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                                {
                                    // Copy screen content into the bitmap
                                    using (Graphics g = Graphics.FromImage(bitmap))
                                    {
                                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                                    }

                                    // Save the screenshot as a PNG (you can change format)
                                    string screenshotName = $"{iterationName}_ETABS_View.png";
                                    string screenshotPath = Path.Combine(folderPath1, screenshotName);

                                    bitmap.Save(screenshotPath, ImageFormat.Png);

                                    Console.WriteLine($"Screenshot saved to {screenshotPath}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("ETABS window could not be found for taking a screenshot.");
                            }

                            #endregion

                            sapModel.SetModelIsLocked(false); // Unlock the model to allow changes

                            sapModel.Analyze.SetRunCaseFlag("PUSHOVER", true); // Enable Pushover Analysis
                            sapModel.Analyze.SetRunCaseFlag("IDA", false); // Enable Pushover Analysis

                            Console.WriteLine("\n-------------------------------------------" +
                                                "\nNow Running Analysis.... for Pushover\n" +
                                                    "-------------------------------------------\n");

                            sapModel.Analyze.RunAnalysis();

                            // Get the results for the Pushover analysis


                            cDatabaseTables myTables_local_PO = sapModel.DatabaseTables;
                            // Define the name of the table to retrieve
                            string tableName_local_PO = "Joint Displacements";

                            // Initialize variables to hold the table data
                            int tableVersion_local_PO = 0;
                            string[] fieldKeyList_local_PO = null;
                            int numRecords_local_PO = 0;
                            string[] tableData_local_PO = null;
                            string[] tableDataHeaders_local_PO = null;

                            // Select the load combination for which you want to retrieve results
                            sapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();
                            //sapModel.Results.Setup.SetComboSelectedForOutput("1.4D");

                            // Retrive the table for display
                            string[] loadCases = { "PUSHOVER" }; // Set the load case to PUSHOVER


                            // Get results tables


                            myTables_local_PO.SetOutputOptionsForDisplay(true, 0, 0, 0, false, 0, 0, false, 0, 0, 3, 2, 3, 3, 2);
                            myTables_local_PO.SetLoadCasesSelectedForDisplay(ref loadCases); // Set the load case for display

                            #region Saving Joint Displacements

                            myTables_local_PO.GetTableForDisplayArray(tableName_local_PO, ref fieldKeyList_local_PO, "",
                                ref tableVersion_local_PO, ref tableDataHeaders_local_PO, ref numRecords_local_PO, ref tableData_local_PO);

                            Console.WriteLine($"\n--- Successfully retrieved table: {tableName_local_PO} for Combo: PUSHOVER ---");




                            Dataall.Clear();
                            Dataall.Add(tableDataHeaders_local_PO); // Add headers as the first row
                            for (int j = 0; j < tableData_local_PO.Length; j += tableDataHeaders_local_PO.Length)
                            {
                                string[] dat = new string[tableDataHeaders_local_PO.Length];
                                for (int k = j; k < (j + tableDataHeaders_local_PO.Length); k++)
                                {
                                    dat[k - j] = tableData_local_PO[k];
                                }
                                Dataall.Add(dat);
                            }


                            // Convert the updated Dataall to a MATLAB cell array
                            matlabCellData = new object[Dataall.Count, Dataall[0].Length];
                            for (int j = 0; j < Dataall.Count; j++)
                            {
                                for (int k = 0; k < Dataall[0].Length; k++)
                                {
                                    matlabCellData[j, k] = Dataall[j][k];
                                }
                            }





                            #endregion

                            #region Saving Base Reaction

                            // Define the name of the table to retrieve
                            string tableName_local_PO_base = "Base Reactions";
                            int tableVersion_local_PO_base = 0;
                            string[] fieldKeyList_local_PO_base = null;
                            int numRecords_local_PO_base = 0;
                            string[] tableData_local_PO_base = null;
                            string[] tableDataHeaders_local_PO_base = null;


                            myTables_local_PO.GetTableForDisplayArray(tableName_local_PO_base, ref fieldKeyList_local_PO_base, "All",
                                ref tableVersion_local_PO_base, ref tableDataHeaders_local_PO_base, ref numRecords_local_PO_base, ref tableData_local_PO_base);

                            Console.WriteLine($"\n--- Successfully retrieved table: {tableName_local_PO_base} for Combo: PUSHOVER ---");


                            var DataAll_base = new List<string[]>();
                            DataAll_base.Clear();
                            DataAll_base.Add(tableDataHeaders_local_PO_base); // Add headers as the first row
                            for (int j = 0; j < tableData_local_PO_base.Length; j += tableDataHeaders_local_PO_base.Length)
                            {
                                string[] dat = new string[tableDataHeaders_local_PO_base.Length];
                                for (int k = j; k < (j + tableDataHeaders_local_PO_base.Length); k++)
                                {
                                    dat[k - j] = tableData_local_PO_base[k];
                                }
                                DataAll_base.Add(dat);
                            }


                            // Convert the updated Dataall to a MATLAB cell array
                            var matlabCellData_base = new object[DataAll_base.Count, DataAll_base[0].Length];
                            for (int j = 0; j < DataAll_base.Count; j++)
                            {
                                for (int k = 0; k < DataAll_base[0].Length; k++)
                                {
                                    matlabCellData_base[j, k] = DataAll_base[j][k];
                                }
                            }


                            #endregion

                            #region Saving Hinge Results

                            // Define the name of the table to retrieve
                            string tableName_local_PO_hinge = "Hinge States";
                            // Initialize variables to hold the table data
                            int tableVersion_local_PO_hinge = 0;
                            string[] fieldKeyList_local_PO_hinge = null;
                            int numRecords_local_PO_hinge = 0;
                            string[] tableData_local_PO_hinge = null;
                            string[] tableDataHeaders_local_PO_hinge = null;


                            myTables_local_PO.GetTableForDisplayArray(tableName_local_PO_hinge, ref fieldKeyList_local_PO_hinge, "All",
                                ref tableVersion_local_PO_hinge, ref tableDataHeaders_local_PO_hinge, ref numRecords_local_PO_hinge, ref tableData_local_PO_hinge);

                            Console.WriteLine($"\n--- Successfully retrieved table: {tableName_local_PO_hinge} for Combo: PUSHOVER ---");


                            var DataAll_hinge = new List<string[]>();
                            DataAll_hinge.Clear();
                            DataAll_hinge.Add(tableDataHeaders_local_PO_hinge); // Add headers as the first row
                            for (int j = 0; j < tableData_local_PO_hinge.Length; j += tableDataHeaders_local_PO_hinge.Length)
                            {
                                string[] dat = new string[tableDataHeaders_local_PO_hinge.Length];
                                for (int k = j; k < (j + tableDataHeaders_local_PO_hinge.Length); k++)
                                {
                                    dat[k - j] = tableData_local_PO_hinge[k];
                                }
                                DataAll_hinge.Add(dat);
                            }


                            // Convert the updated Dataall to a MATLAB cell array
                            var matlabCellData_hinge = new object[DataAll_hinge.Count, DataAll_hinge[0].Length];
                            for (int j = 0; j < DataAll_hinge.Count; j++)
                            {
                                for (int k = 0; k < DataAll_hinge[0].Length; k++)
                                {
                                    matlabCellData_hinge[j, k] = DataAll_hinge[j][k];
                                }
                            }



                            // save the mat file
                            matlab.Execute("cd('" + folderPath + "')"); // Change directory to the folder where the model is saved
                            matlab.Execute("save('etabs_results.mat')");

                            #endregion

                            #region Processing Pushover Data in MATLAB
                            // Sending the data to MATLAB
                            matlab.PutWorkspaceData("Pushover_JointDisplacement", "base", matlabCellData);

                            // Sending the Pushover_BaseReaction to MATLAB
                            matlab.PutWorkspaceData("Pushover_BaseReaction", "base", matlabCellData_base);

                            // Sending the Pushover_HingeResults to MATLAB
                            matlab.PutWorkspaceData("Pushover_HingeResults", "base", matlabCellData_hinge);

                            #region BaseShear Table

                            // Chaning to BaseSheaer Table
                            matlab.Execute("pushover_basreaction = cell2table(Pushover_BaseReaction(2:end,[4,5]));");

                            // Renaming the Variables
                            matlab.Execute("pushover_basreaction.Properties.VariableNames  = {'Step','BaseShear'};");

                            // Changing the Variables to Numeric
                            matlab.Execute("pushover_basreaction.Step = str2double(pushover_basreaction.Step);");
                            matlab.Execute("pushover_basreaction.BaseShear = str2double(pushover_basreaction.BaseShear);");

                            #endregion

                            #region Joint Displacement Table and BaseShear Table Joining

                            // Chaning to Joint Displacement Table
                            matlab.Execute("Variablenames = string(Pushover_JointDisplacement(1,:));");

                            matlab.Execute("Pushover_JointDisplacement(1,:) = [];");

                            matlab.Execute("Pushover_JointDisplacement = cell2table(Pushover_JointDisplacement);");

                            matlab.Execute("Pushover_JointDisplacement.Properties.VariableNames = Variablenames;");

                            matlab.Execute("n = height(Pushover_JointDisplacement);");
                            matlab.Execute("Pushover_JointDisplacement.Step = mod((0:n-1)',height(pushover_basreaction.Step))");

                            matlab.Execute("pushover_jointDisplacement = Pushover_JointDisplacement;");

                            matlab.Execute("pushover_jointDisplacement.Ux = str2double(pushover_jointDisplacement.Ux);");
                            matlab.Execute("pushover_jointDisplacement.Ux(1) = 0;");

                            // Changing the Variables to Numeric
                            matlab.Execute("pushover_jointDisplacement.Step = str2double(pushover_jointDisplacement.Step);");

                            matlab.Execute("pushover_jointDisplacement_maxUx = groupsummary(pushover_jointDisplacement,{'Story','Step'},'max','Ux');");

                            matlab.Execute("pushover_jointDisplacement_maxUx.GroupCount = [];");

                            matlab.Execute("pushover_basreaction.Step = categorical(pushover_basreaction.Step);");

                            matlab.Execute("pushover_jointDisplacement_maxUx.Step = categorical(pushover_jointDisplacement_maxUx.Step);");

                            matlab.Execute("Pushover_Data = innerjoin(pushover_jointDisplacement_maxUx, pushover_basreaction, 'Keys', 'Step');");

                            matlab.Execute("StoryData{1,1} = 'Base'");

                            matlab.Execute("S1 = cell2table(StoryData);");

                            matlab.Execute("S1.Properties.VariableNames = {'Story', 'Elevation','Height'}");

                            matlab.Execute("S1.Story = categorical(S1.Story);");

                            matlab.Execute("Pushover_Data.Story = categorical(Pushover_Data.Story);");

                            matlab.Execute("Pushover_Data = innerjoin(Pushover_Data, S1, 'Keys', 'Story');");

                            matlab.Execute("Pushover_Data.Elevation = str2double(Pushover_Data.Elevation)");

                            matlab.Execute("Pushover_Data.Height = str2double(Pushover_Data.Height)");

                            matlab.Execute("[~,maxIdx] = max(Pushover_Data.Elevation);");

                            matlab.Execute("Pushover_Data = Pushover_Data(Pushover_Data.Story == Pushover_Data.Story(maxIdx), :);");

                            matlab.Execute("Pushover_Data.DriftRatio = Pushover_Data.max_Ux ./ Pushover_Data.Elevation;");

                            matlab.Execute("Pushover_Data.BaseShear = - Pushover_Data.BaseShear;");

                            matlab.Execute("Pushover_Data.DriftRatio = Pushover_Data.DriftRatio * 100;");


                            #endregion

                            #region Plotting the Pushover Curve

                            // Making scatter plot of Drift Ratio vs. Base Shear
                            matlab.Execute("PushOverCurve = figure('Name', 'Pushover Curve', 'NumberTitle', 'off');");
                            matlab.Execute("PushOverCurve.WindowState = 'maximized'");
                            matlab.Execute("plot(Pushover_Data.DriftRatio, Pushover_Data.BaseShear, '--o', 'LineWidth', 1, 'DisplayName', 'Pushover Data from ETABs');");
                            matlab.Execute("legend show");
                            // Adding legends as original points

                            matlab.Execute("grid on;");
                            matlab.Execute("xlabel('Roof Drift Ratio (%)');");
                            matlab.Execute("ylabel('Base Shear (kN)');");
                            matlab.Execute("title('Pushover Curve: Base Shear vs. Roof Drift Ratio');");
                            matlab.Execute("xlim([0, max(Pushover_Data.DriftRatio)*1.1]);");
                            matlab.Execute("ylim([0, max(Pushover_Data.BaseShear)*1.1]);");
                            matlab.Execute("hold on;");

                            // Fitting a polynomial curve to the data
                            //matlab.Execute("p = polyfit(Pushover_Data.DriftRatio,Pushover_Data.BaseShear,height(Pushover_Data));");



                            #endregion

                            #region Finding the Hinge Locations and Step Numbers at which it is achieved

                            matlab.Execute("pushover_hinge = cell2table(Pushover_HingeResults(2:end,[7,width(Pushover_HingeResults) - 1]));");

                            matlab.Execute("pushover_hinge.Properties.VariableNames = {'Step','HingeStates'};");

                            matlab.Execute("pushover_hinge.Step = str2double(pushover_hinge.Step);");

                            matlab.Execute("zeroRows_StepNumber = find(pushover_hinge.Step == 0);");

                            matlab.Execute("refBlock = pushover_hinge((zeroRows_StepNumber(1)):zeroRows_StepNumber(3)-1,:);");

                            matlab.Execute("blockSize = zeroRows_StepNumber(3) - zeroRows_StepNumber(1);");

                            matlab.Execute("nBlocks = floor(height(pushover_hinge) / blockSize);");

                            matlab.Execute("col2 = pushover_hinge{:,2};");

                            matlab.Execute("colBlocks = reshape(col2,blockSize, nBlocks);");

                            matlab.Execute("newCols = array2table(colBlocks,'VariableNames', strcat(\"Frame\", string(1:nBlocks)));");

                            matlab.Execute("pushover_hinge_new = [pushover_hinge(1:blockSize,1) newCols];");

                            matlab.Execute("frameCols = pushover_hinge_new(:,2:end-1);");

                            matlab.Execute("frameCells = table2cell(frameCols);");

                            matlab.Execute("order = [\"A to B\",\"B to C\",\"C to D\",\"D to E\",\">E\"]");

                            matlab.Execute("catFrames = categorical(frameCells, order, 'Ordinal', true);");

                            matlab.Execute("codes = double(catFrames);");

                            matlab.Execute("mask3D = permute(codes, [1 2 3]) == reshape(1:numel(order), 1, 1,[]);");

                            matlab.Execute("[~,idx] = max(mask3D, [], 2);");

                            matlab.Execute("idx = squeeze(idx);");

                            matlab.Execute("exits = squeeze(any(mask3D, 2));");

                            matlab.Execute("idx(~exits) = 0;");

                            matlab.Execute("firstOccTable = array2table(idx, 'VariableNames', \"Frame_\" + order);");

                            matlab.Execute("pushover_hinge_final = [pushover_hinge_new firstOccTable];");


                            // %% Assumes you already have:
                            //% -pushover_hinge_final(table) with columns:
                            //% 'Step','Frame_A to B','Frame_B to C','Frame_C to D','Frame_D to E','Frame_>E'
                            //% -Pushover_Data.DriftRatio(vector) where index = Step + 1

                            // 1) Convenience aliases
                            matlab.Execute("S  = pushover_hinge_final.Step;");
                            matlab.Execute("AB = pushover_hinge_final.('Frame_A to B') > 0;");
                            matlab.Execute("BC = pushover_hinge_final.('Frame_B to C') > 0;");
                            matlab.Execute("CD = pushover_hinge_final.('Frame_C to D') > 0;");
                            matlab.Execute("DE = pushover_hinge_final.('Frame_D to E') > 0;");
                            matlab.Execute("GE = pushover_hinge_final.('Frame_>E')     > 0;");

                            //2) DS1 (A->B) first occurrence (no prior threshold)
                            matlab.Execute("v1 = S; v1(~(AB & (S > -inf))) = inf;");
                            matlab.Execute("StepS1 = min(v1); StepS1(StepS1==inf) = NaN;");

                            //3) DS2 (B->C) after latest of previous states (here just DS1)
                            matlab.Execute("th2 = StepS1; th2(isnan(th2)) = -inf;");
                            matlab.Execute("v2 = S; v2(~(BC & (S > th2))) = inf;");
                            matlab.Execute("StepS2 = min(v2); StepS2(StepS2==inf) = NaN;");

                            //4) DS3 (C->D) after latest of [DS1, DS2]  (fallback if DS2 missing)
                            matlab.Execute("th3 = max([StepS1, StepS2], [], 'omitnan'); th3(isnan(th3)) = -inf;");
                            matlab.Execute("v3 = S; v3(~(CD & (S > th3))) = inf;");
                            matlab.Execute("StepS3 = min(v3); StepS3(StepS3==inf) = NaN;");

                            // 5) DS4 (D->E) after latest of [DS1, DS2, DS3] (fallback if DS3 missing)
                            matlab.Execute("th4 = max([StepS1, StepS2, StepS3], [], 'omitnan'); th4(isnan(th4)) = -inf;");
                            matlab.Execute("v4 = S; v4(~(DE & (S > th4))) = inf;");
                            matlab.Execute("StepS4 = min(v4); StepS4(StepS4==inf) = NaN;");

                            // 6) DS5 (>E) after latest of [DS1, DS2, DS3, DS4] (fallback if DS4 missing)
                            matlab.Execute("th5 = max([StepS1, StepS2, StepS3, StepS4], [], 'omitnan'); th5(isnan(th5)) = -inf;");
                            matlab.Execute("v5 = S; v5(~(GE & (S > th5))) = inf;");
                            matlab.Execute("StepS5 = min(v5); StepS5(StepS5==inf) = NaN;");

                            // 7) Collect sequential steps
                            matlab.Execute("StepStates = [StepS1, StepS2, StepS3, StepS4, StepS5];");

                            // 8) Compute drift-ratio damage states without branching
                            matlab.Execute("idxs = StepStates + 1;");
                            matlab.Execute("valid = ~isnan(idxs) & idxs>=1 & idxs<=numel(Pushover_Data.DriftRatio);");
                            matlab.Execute("DamageS = NaN(1,5);");
                            matlab.Execute("tmp = NaN(1,5); tmp(valid) = Pushover_Data.DriftRatio(idxs(valid));");
                            matlab.Execute("DamageS(:) = tmp;");


                            // DamageSTATES
                            matlab.Execute("DamageS1 = DamageS(1);");
                            matlab.Execute("DamageS2 = DamageS(2);");
                            matlab.Execute("DamageS3 = DamageS(3);");
                            matlab.Execute("DamageS4 = DamageS(4);");
                            matlab.Execute("DamageS5 = DamageS(5);");


                            matlab.Execute("DamageStates = [Pushover_Data.DriftRatio(2), DamageS2, DamageS3, DamageS4, DamageS5];");

                            matlab.Execute("valid = ~isnan(idxs) & idxs>=1 & idxs<=numel(Pushover_Data.BaseShear);");
                            matlab.Execute("ys = NaN(1,5);");
                            matlab.Execute("ys(valid) = Pushover_Data.BaseShear(idxs(valid));");
                            matlab.Execute("ys(1) = Pushover_Data.BaseShear(2)");

                            matlab.Execute("ys = interp1(Pushover_Data.DriftRatio,Pushover_Data.BaseShear,DamageStates,'linear','extrap');");


                            //// Calculating Points Again
                            //matlab.Execute("p = inputParser;");
                            //matlab.Execute("addParameter(p, 'ResidualRatio', 0.2);");
                            //matlab.Execute("addParameter(p, 'UltimateFactor', 4);");
                            //matlab.Execute("addParameter(p, 'ResidualDriftFactor', 2);");
                            //matlab.Execute("parse(p);");

                            //// DS2 Calculation
                            //matlab.Execute("if isnan(ys(2)), [~, idxB] = min(abs(Pushover_Data.BaseShear - 0.8 * max(Pushover_Data.BaseShear))); ys(2) = Pushover_Data.BaseShear(idxB); end");
                            //matlab.Execute("if isnan(DamageStates(2)), DamageStates(2) = interp1(Pushover_Data.BaseShear, Pushover_Data.DriftRatio, ys(2), 'linear', 'extrap'); end");

                            //// DS3 Caluclation
                            //matlab.Execute("if isnan(ys(3)), [ys(3), idxC] = max(Pushover_Data.BaseShear); else, [~, idxC] = min(abs(Pushover_Data.BaseShear - ys(3))); end");
                            //matlab.Execute("DamageStates(3) = Pushover_Data.DriftRatio(idxC);");

                            //matlab.Execute("if ys(2) < Pushover_Data.BaseShear(end), DamageStates(3) = DamageStates(3) * 2.5; end");


                            //// DS4 Calculation
                            //matlab.Execute("if isnan(DamageStates(4)), DamageStates(4) = DamageStates(3) * p.Results.ResidualDriftFactor;end");
                            //matlab.Execute("if isnan(ys(4)), ys(4) = ys(3) * p.Results.ResidualRatio;end");

                            //// DS5 Ultimate Deformation
                            //matlab.Execute("if isnan(DamageStates(5)),DamageStates(5) = DamageStates(3) * p.Results.UltimateFactor;end");
                            //matlab.Execute("ys(5) = ys(4);");


                            //matlab.Execute("DamageS1 = DamageStates(1);");
                            //matlab.Execute("DamageS2 = DamageStates(2);");
                            //matlab.Execute("DamageS3 = DamageStates(3);");
                            //matlab.Execute("DamageS4 = DamageStates(4);");
                            //matlab.Execute("DamageS5 = DamageStates(5);");

                            //matlab.Execute("y_DS1 = ys(1);");
                            //matlab.Execute("y_DS2 = ys(2);");
                            //matlab.Execute("y_DS3 = ys(3);");
                            //matlab.Execute("y_DS4 = ys(4);");
                            //matlab.Execute("y_DS5 = ys(5);");



                            // --- Calculating Points Again (First Inflection Yield & No Artificial Hardening) ---
                            matlab.Execute("p = inputParser;");
                            matlab.Execute("addParameter(p, 'ResidualRatio', 0.2);");
                            matlab.Execute("addParameter(p, 'UltimateFactor', 4);");
                            matlab.Execute("addParameter(p, 'ResidualDriftFactor', 2);");
                            matlab.Execute("parse(p);");

                            // 1. Identify Peak Capacity (DS3) - simply the maximum shear found
                            // We do NOT use 'findpeaks' here to avoid confusion with double humps. 
                            // The max capacity is simply the highest point the structure reached.
                            matlab.Execute("[V_peak, idx_peak] = max(Pushover_Data.BaseShear);");
                            matlab.Execute("ys(3) = V_peak;");
                            matlab.Execute("DamageStates(3) = Pushover_Data.DriftRatio(idx_peak);");

                            // 2. Find Yield (DS2) - The "First Inflection" / Stiffness Drop
                            // Calculate stiffness (slope) at every point
                            matlab.Execute("stiffness = diff(Pushover_Data.BaseShear) ./ diff(Pushover_Data.DriftRatio);");
                            // Smooth it slightly to avoid noise spikes
                            matlab.Execute("stiffness = smooth(stiffness, 5);");

                            // Initial Stiffness is the max stiffness found in the early elastic region (first 10% of points up to peak)
                            matlab.Execute("k_initial = max(stiffness(1:max(2, floor(idx_peak/5))));");

                            // Find the first point where stiffness drops below 40% of initial stiffness (Yielding)
                            matlab.Execute("idx_yield = find(stiffness(1:idx_peak) < 0.4 * k_initial, 1, 'first');");

                            // Safety Fallback: If no clear drop found, use the 75% Max Force rule
                            matlab.Execute("if isempty(idx_yield), [~, idx_yield] = min(abs(Pushover_Data.BaseShear(1:idx_peak) - 0.75 * V_peak)); end");

                            // Assign DS2
                            matlab.Execute("idx_yield = max(2, idx_yield);"); // Ensure index is at least 2
                            matlab.Execute("ys(2) = Pushover_Data.BaseShear(idx_yield);");
                            matlab.Execute("DamageStates(2) = Pushover_Data.DriftRatio(idx_yield);");

                            // 3. Separation Check
                            // If Yield (DS2) is indistinguishable from Elastic (DS1), force a small separation
                            matlab.Execute("if DamageStates(2) < 1.25 * DamageStates(1), DamageStates(2) = 1.25 * DamageStates(1); end");
                            // Update force to match new drift
                            matlab.Execute("ys(2) = interp1(Pushover_Data.DriftRatio, Pushover_Data.BaseShear, DamageStates(2), 'linear', 'extrap');");

                            // 4. DS4 (Residual) & DS5 (Collapse)
                            matlab.Execute("if isnan(DamageStates(4)), DamageStates(4) = DamageStates(3) * p.Results.ResidualDriftFactor; end");
                            matlab.Execute("if isnan(ys(4)), ys(4) = ys(3) * p.Results.ResidualRatio; end");

                            matlab.Execute("if isnan(DamageStates(5)), DamageStates(5) = DamageStates(3) * p.Results.UltimateFactor; end");
                            matlab.Execute("ys(5) = ys(4);"); // Flat plateau

                            // Assign Output Variables
                            matlab.Execute("DamageS1 = DamageStates(1);");
                            matlab.Execute("DamageS2 = DamageStates(2);");
                            matlab.Execute("DamageS3 = DamageStates(3);");
                            matlab.Execute("DamageS4 = DamageStates(4);");
                            matlab.Execute("DamageS5 = DamageStates(5);");

                            matlab.Execute("y_DS1 = ys(1);");
                            matlab.Execute("y_DS2 = ys(2);");
                            matlab.Execute("y_DS3 = ys(3);");
                            matlab.Execute("y_DS4 = ys(4);");
                            matlab.Execute("y_DS5 = ys(5);");

                            #endregion

                            #region Plotting the Damage States (DS) to the Pushover Curve


                            matlab.Execute("x_fits = linspace(0,DamageS5,100)");
                            matlab.Execute("BaseShear_fit = polyval(p,x_fits);");
                            matlab.Execute("plot(x_fits,BaseShear_fit, 'r-', 'LineWidth', 2, 'DisplayName', 'Fitted Polynomial 7th Order');");

                            //scattering the Damage States on the Pushover Curve
                            matlab.Execute("spline_x = linspace(DamageStates(1), DamageStates(5), 200);");
                            matlab.Execute("spline_y = pchip(DamageStates, ys, spline_x);");


                            matlab.Execute("plot(spline_x, spline_y, '-k', 'LineWidth', 2, 'DisplayName', 'Idealized Pushover Curve');");




                            matlab.Execute("scatter(DamageS1, y_DS1, 100, 'filled', 'DisplayName', sprintf('DS1: %.3f%%', DamageS1));");
                            matlab.Execute("scatter(DamageS2, y_DS2, 100, 'filled', 'DisplayName', sprintf('DS2: %.3f%%', DamageS2));");
                            matlab.Execute("scatter(DamageS3, y_DS3, 100, 'filled', 'DisplayName', sprintf('DS3: %.3f%%', DamageS3));");
                            matlab.Execute("scatter(DamageS4, y_DS4, 100, 'filled', 'DisplayName', sprintf('DS4: %.3f%%', DamageS4));");
                            matlab.Execute("scatter(DamageS5, y_DS5, 100, 'filled', 'DisplayName', sprintf('DS5: %.3f%%', DamageS5));");

                            
                            // limiting the x-axis to DamageS5
                            matlab.Execute("xlim([0, max(DamageStates)]);");

                            // limiting the y-axis to maximum value for BaseShear_fit
                            matlab.Execute("ylim([0, max(max(Pushover_Data.BaseShear),max(ys))]);");


                            matlab.Execute("hold off");

                            #endregion

                            #endregion


                            //matlab.PutWorkspaceData("StoryHeights",,)

                            Console.WriteLine("\nData sent to MATLAB successfully.");

                            matlab.Execute("DamageS1 = DamageS1 / 100;");
                            matlab.Execute("DamageS2 = DamageS2 / 100;");
                            matlab.Execute("DamageS3 = DamageS3 / 100;");
                            matlab.Execute("DamageS4 = DamageS4 / 100;");
                            matlab.Execute("DamageS5 = DamageS5 / 100;");


                            // Getting Damage States values from matlab
                            object DS1_IDR = null;
                            object DS2_IDR = null;
                            object DS3_IDR = null;
                            object DS4_IDR = null;
                            object DS5_IDR = null;
                            matlab.GetWorkspaceData("DamageS1", "base", out DS1_IDR);
                            matlab.GetWorkspaceData("DamageS2", "base", out DS2_IDR);
                            matlab.GetWorkspaceData("DamageS3", "base", out DS3_IDR);
                            matlab.GetWorkspaceData("DamageS4", "base", out DS4_IDR);
                            matlab.GetWorkspaceData("DamageS5", "base", out DS5_IDR);

                            double damage_limit = double.Parse(DS5_IDR.ToString());

                            string filename_pushover = $"{iterationName}_PushOverCurve.png";

                            string fullpath_pushover = Path.Combine(folderPath1, filename_pushover);

                            matlab.Execute($"saveas(PushOverCurve, '{fullpath_pushover}');");

                            matlab.Execute("PushOverCurve.Visible = 'off';");

                            #endregion

                            #region Going Over the Different Functions and Scaling up For IDA and Fragility

                            double factor = 9806.65; // Conversion factor for g to mm/s^2

                            double SF = 0.1 * factor; // Scale Factor Initiation

                            double max_Acceleration = 100 * factor; // 1g

                            // Plot the Maximum Vertical Displacement against Column Size
                            List<string> legendEntries = new List<string>();
                            List<string> idr_names_list = new List<string>();
                            List<string> sf_names_list = new List<string>();
                            List<string> func_name_accelerations_names = new List<string>();
                            string idr_name = null;
                            string SF_name = null;
                            string func_name_accelerations = null;
                            string func_max = null;
                            string legend = null;

                            if (firstTimeRun)
                            {
                                if (functionCounter > 0)
                                {
                                    for (int i = 0; i < functionCounter; i++)
                                    {
                                        idr_name = functions[i].Name + "_IDRs";
                                        SF_name = functions[i].Name + "_SFArray";
                                        func_name_accelerations = functions[i].Name + "_FuncMax";
                                        func_max = functions[i].maxValue.ToString();
                                        legend = functions[i].Name.ToString();

                                        sf_names_list.Add(SF_name);
                                        idr_names_list.Add(idr_name);
                                        func_name_accelerations_names.Add(func_name_accelerations);
                                        legendEntries.Add(legend); // Assuming 'func' has a Name property
                                    }
                                }
                            }

                            matlab.Execute("close 'ETABS Capacity Curves Plot';");
                            matlab.Execute("figure1 = figure('Name', 'ETABS Capacity Curves Plot', 'NumberTitle', 'off');");
                            matlab.Execute("figure1.WindowState = 'maximized';");

                            //e2tFilePath = Path.Combine(backupFolderPath, "ETABs_Plugin_Backup.$et");

                            //sapModel.File.ImportFile(e2tFilePath, eFileTypeIO.TextFile, 1); // Reopen the ETABs file to refresh the model
                            //sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD"); // Save the model in the new folder
                            matlab.Execute($"colors = hsv({functions.Count + 3});");


                            for (int f = functionCounter; f < functions.Count; f++)
                            {
                                var func = functions[f];
                                functionCounter = f;

                                /*% GET_SPECTRAL_ACCELERATION Computes Sa(T) for a given accelerogram
                                     %
                                    % Inputs:
                                    %   acc_values - Vector of ground acceleration values (mm/s^2 or g)
                                    %   dt         - Time step of the record (seconds)
                                    %   T_building - Fundamental period of the structure (seconds)
                                 */

                                matlab.Execute("damping = 0.05;"); //% Standard 5% structural damping
                                matlab.Execute("omega = 2 * pi / Fundamental_Time_Period;");

                                //% Define the time vector based on length of record
                                matlab.PutWorkspaceData("dt", "base", functions[f].TimeValues);
                                matlab.Execute("dt = dt(3) - dt(2);");

                                matlab.PutWorkspaceData("acc_values_raw", "base", functions[f].Value);

                                matlab.Execute($"acc_values_normalized = acc_values_raw ./ {functions[f].maxValue};");


                                matlab.Execute("t = (0:length(acc_values_normalized)-1) * dt;");
                                //% Define the SDOF (Single Degree of Freedom) System
                                //% Equation of motion: m* u'' + c * u' + k*u = -m*ug''
                                matlab.Execute("m = 1;");
                                matlab.Execute("k = omega^2 * m;");
                                matlab.Execute("c = 2 * damping * omega * m;");


                                //% Create the Transfer Function system
                                //% sys = Output / Input = 1 / (ms ^ 2 + cs + k)
                                matlab.Execute("sys = tf(1,[m c k]);");


                                //% Solve for relative displacement response (u)
                                //% lsim simulates the time response of the linear system
                                matlab.Execute("u = lsim(sys, -acc_values_normalized, t);");

                                //% Compute Spectral Acceleration (Pseudo-acceleration)
                                //% Sa = omega ^ 2 * max(| u |)
                                //% This value will have the same units as your input acc_values(g or mm/ s ^ 2)
                                matlab.Execute("Sa = max(abs(u)) * omega^2;");

                                object Sa = null;
                                matlab.GetWorkspaceData("Sa", "base", out Sa);

                                #region Initial Stuff

                                // Initializing the Scale Factor and IDR names
                                SF = 0.1 * factor;

                                idr_name = func.Name + "_IDRs";
                                SF_name = func.Name + "_SFArray";
                                func_name_accelerations = func.Name + "_FuncMax";
                                func_max = func.maxValue.ToString();

                                sf_names_list.Add(SF_name);
                                idr_names_list.Add(idr_name);
                                func_name_accelerations_names.Add(func_name_accelerations);
                                legendEntries.Add(func.Name); // Assuming 'func' has a Name property
                                



                                matlab.Execute($"{idr_name} = [0];"); // Clear the figure before plotting
                                matlab.Execute($"{SF_name} = [0];"); // Clear the figure before plotting
                                matlab.Execute($"{func_name_accelerations} = [0 * {func_max}]");

                                #endregion

                                //double Limit = 0.05; // Limit for IDR, can be adjusted

                                #region Iteration for Scale Factor Change and Replotting

                                double IDR = 0;

                                int slope_flag = 1;
                                int slope_flag_first_time = 1;

                                //int Number_Of_Steps = 10;

                                //double startSF = 0.1 * func.maxValue;
                                //double endSF = func.maxValue;

                                //double increment = (endSF - startSF) / (Number_Of_Steps - 1);
                                 
                                //double slace_factor_flag = SF * func.maxValue;
                                int number_of_step = 10;

                                double increment = (max_Acceleration / 100) / number_of_step;


                                for (double i = 0; (SF + i) < max_Acceleration; i = i + (increment))
                                {
                                    //functionpoint = i;

                                    if (firstTimeRun == true && (functionCounter != 0 ))//|| functionpoint != 0))
                                    {
                                        matlab.Execute("figsBefore = findall(0, 'Type', 'figure');");

                                        matlab.Execute("cd('" + backupFolderPath + "')"); // Change directory to the folder where the model is saved
                                        matlab.Execute("load('etabs_results.mat');");

                                        matlab.Execute("figsAfter = findall(0, 'Type', 'figure');");

                                        matlab.Execute("newFigs = setdiff(figsAfter, figsBefore);");
                                        matlab.Execute("close(newFigs)");

                                        matlab.Execute("close 'ETABS Capacity Curves Plot';");

                                        matlab.Execute("figure1 = figure('Name', 'ETABS Capacity Curves Plot', 'NumberTitle', 'off');");
                                        matlab.Execute("figure1.WindowState = 'maximized';");


                                        matlab.Execute($"DamageStates = [{DS1_IDR} * 100,{DS2_IDR} * 100,{DS3_IDR} * 100,{DS4_IDR} * 100,{DS5_IDR} * 100]");
                                        matlab.Execute($"DamageS1 = DamageStates(1) / 100;");
                                        matlab.Execute($"DamageS2 = DamageStates(2) / 100;");
                                        matlab.Execute($"DamageS3 = DamageStates(3) / 100;");
                                        matlab.Execute($"DamageS4 = DamageStates(4) / 100;");
                                        matlab.Execute($"DamageS5 = DamageStates(5) / 100;");

                                        matlab.Execute($"{idr_name} = [0];"); // Clear the figure before plotting
                                        matlab.Execute($"{SF_name} = [0];"); // Clear the figure before plotting
                                        matlab.Execute($"{func_name_accelerations} = [0 * {func_max}]");
                                    }


                                    File.WriteAllText(etabsDataFilePath, string.Empty);
                                    // Update the ETABS_Data.txt file with the current storey, g_x, and g_y
                                    using (StreamWriter sw = new StreamWriter(etabsDataFilePath, true))
                                    {
                                        sw.WriteLine($"{storey}");
                                        sw.WriteLine($"{g_x}");
                                        sw.WriteLine($"{g_y}");
                                        sw.WriteLine($"{iteration}");
                                        sw.WriteLine($"{functionCounter}");
                                        //sw.WriteLine($"{functionpoint}");
                                    }
                                    //slace_factor_flag = slace_factor_flag * i;
                                    Sub_SNo++; // Increment Sub_SNo for each iteration
                                    if (IDR > damage_limit)
                                    {
                                        // If IDR exceeds the limit, break the loop
                                        Console.WriteLine($"IDR exceeded limit ({damage_limit}) for function {func.Name} at scale factor {SF + i}. Stopping further iterations.");
                                        matlab.Execute($"{idr_name}(n) = []");
                                        matlab.Execute($"{func_name_accelerations}(n) = []");
                                        break;
                                    }

                                    #region Changing the Scale Factor

                                    //double scale_factor_temp = startSF + i * increment;


                                    double[] scaleFactorLocal = { SF + i };//{SF * i};

                                    Sa = (double)Sa * scaleFactorLocal[0] / factor;

                                    //scale_factor_temp = scaleFactorLocal[0];
                                    //check_factor_temp = (scaleFactorLocal[0] / factor) * func.maxValue;


                                    // Unlock the model to allow changes
                                    sapModel.SetModelIsLocked(false);

                                    foreach (var loadcase in loadCasesInfos)
                                    {
                                        if (loadcase.LoadCaseType == eLoadCaseType.LinearHistory)
                                        {

                                            // 2. Declare variables to hold the "Loads Applied" data
                                            int numberLoads = 0;
                                            string[] loadType = null;
                                            string[] loadName = null;
                                            string[] function = null;
                                            double[] scaleFactor = null;
                                            double[] timeFactor = null;
                                            double[] arrivalTime = null;
                                            string[] coordSystem = null;
                                            double[] angle = null;

                                            // 3. Call the specific method to get the loads for a Time History case
                                            sapModel.LoadCases.ModHistLinear.GetLoads(loadcase.Name, ref numberLoads, ref loadType,
                                                ref loadName, ref function, ref scaleFactor, ref timeFactor, ref arrivalTime,
                                                ref coordSystem, ref angle);

                                            string[] function_new = { func.Name };

                                            sapModel.LoadCases.ModHistLinear.SetLoads(loadcase.Name, numberLoads, ref loadType,
                                                ref loadName, ref function_new, ref scaleFactorLocal, ref timeFactor, ref arrivalTime,
                                                ref coordSystem, ref angle);
                                        }
                                        else if (loadcase.LoadCaseType == eLoadCaseType.NonlinearHistory || loadcase.LoadCaseType == eLoadCaseType.NonlinearDynamic)
                                        {
                                            Parsing.UpdateNonlinearLoadcase(e2tFilePath, func.Name, scaleFactorLocal[0]);
                                            sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD");

                                            #region Close Etabs and Open it again

                                            etabsObject.ApplicationExit(true);

                                            etabsObject = helper.CreateObjectProgID("CSI.ETABS.API.ETABSObject");
                                            etabsObject.ApplicationStart();


                                            sapModel = etabsObject.SapModel;
                                            sapModel.SetModelIsLocked(false);

                                            sapModel.File.ImportFile(e2tFilePath, eFileTypeIO.TextFile, 1); // Reopen the ETABs file to refresh the model
                                            sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD"); // Save the model in the new folder

                                            sapModel.SetPresentUnits(eUnits.N_mm_C);

                                            #endregion
                                        }
                                    }


                                    #endregion

                                    #region Saving and Rerunning Analysis



                                    // Saving the Model
                                    sapModel.File.Save(folderPath + '\\' + "ETABs_Plugin.EBD");

                                    Console.WriteLine("\nModel saved successfully in the new folder.");

                                    sapModel.Analyze.SetRunCaseFlag("IDA", true); // Enable IDA load case for analysis
                                    sapModel.Analyze.SetRunCaseFlag("PUSHOVER", false); // Disable Pushover Analysis
                                    // Analysing the Model


                                    Console.WriteLine("\n------------------------------------------------------------" +
                                                    "\nNow Running Analysis.... for Incremental Dynamic Analysis\n" +
                                                        "------------------------------------------------------------\n");
                                    sapModel.Analyze.RunAnalysis();

                                    #endregion

                                    #region Getting Results and Sending Data Again to MATLAB

                                    

                                    if (slope_flag == 1)
                                    {
                                        // Get the new column size
                                        // Update the column size in MATLAB
                                        matlab.PutWorkspaceData("SF", "base", scaleFactorLocal[0]);
                                        // Update the acceleration in MATLAB
                                        matlab.Execute($"{SF_name} = [{SF_name}; SF];");
                                        matlab.Execute($"{func_name_accelerations} = ({SF_name}/{factor})");
                                    }
                                    


                                    // Get results tables
                                    cDatabaseTables myTables_local = sapModel.DatabaseTables;

                                    // Define the name of the table to retrieve
                                    string tableName_local = "Joint Displacements";

                                    // Initialize variables to hold the table data
                                    int tableVersion_local = 0;
                                    string[] fieldKeyList_local = null;
                                    int numRecords_local = 0;
                                    string[] tableData_local = null;
                                    string[] tableDataHeaders_local = null;

                                    // Select the load combination for which you want to retrieve results
                                    sapModel.Results.Setup.DeselectAllCasesAndCombosForOutput();
                                    //sapModel.Results.Setup.SetComboSelectedForOutput("1.4D");

                                    // Retrive the table for display
                                    myTables_local.GetTableForDisplayArray(tableName_local, ref fieldKeyList_local, "All", ref tableVersion_local, ref tableDataHeaders_local, ref numRecords_local, ref tableData_local);

                                    Console.WriteLine($"\n--- Successfully retrieved table: {tableName} for Combo: IDA ---");


                                    Dataall.Clear();
                                    Dataall.Add(tableDataHeaders_local); // Add headers as the first row
                                    for (int j = 0; j < tableData_local.Length; j += tableDataHeaders_local.Length)
                                    {
                                        string[] dat = new string[tableDataHeaders_local.Length];
                                        for (int k = j; k < (j + tableDataHeaders_local.Length); k++)
                                        {
                                            dat[k - j] = tableData_local[k];
                                        }
                                        Dataall.Add(dat);
                                    }


                                    // Convert the updated Dataall to a MATLAB cell array
                                    matlabCellData = new object[Dataall.Count, Dataall[0].Length];
                                    for (int j = 0; j < Dataall.Count; j++)
                                    {
                                        for (int k = 0; k < Dataall[0].Length; k++)
                                        {
                                            matlabCellData[j, k] = Dataall[j][k];
                                        }
                                    }

                                    // Sending the storyList to MATLAB
                                    //object[,] storyCellData = new object[storyList.Count, 3];
                                    for (int l = 0;
                                        l < storyList.Count;
                                        l++)
                                    {
                                        string tempname = storyList[l].Name;
                                        string tempelevation = storyList[l].Elevation.ToString();
                                        string tempheight = storyList[l].Height.ToString();

                                        storyCellData[l, 0] = tempname;
                                        storyCellData[l, 1] = tempelevation;
                                        storyCellData[l, 2] = tempheight;

                                    }

                                    // Sending the storyList to MATLAB
                                    matlab.PutWorkspaceData("StoryData", "base", storyCellData);

                                    // Sending the data to MATLAB
                                    matlab.PutWorkspaceData("ETABSData", "base", matlabCellData);
                                    //matlab.PutWorkspaceData("StoryHeights",,)

                                    Console.WriteLine("\nData sent to MATLAB successfully.");
                                    #endregion

                                    #region Processing the Data in MATLAB

                                    matlab.Execute("disp('Processing data and creating plot...')");
                                    matlab.Execute("T = cell2table(ETABSData(2:end,:), 'VariableNames', ETABSData(1,:))");
                                    matlab.Execute("S = cell2table(StoryData(1:end,:))");

                                    matlab.Execute("T = T(T.OutputCase == \"IDA\", :);");
                                    //matlab.Execute("S = S(S.OutputCase == \"IDA\", :);");

                                    matlab.Execute("S.Properties.VariableNames = {'Story', 'Elevation','Height'}");
                                    matlab.Execute("T_Final = innerjoin(T, S, 'Keys', 'Story');");
                                    //matlab.Execute("T_sorted = sortrows(T_with_elev,'Elevation')");

                                    

                                    matlab.Execute("T.Uy = str2double(T.Uy);"); matlab.Execute("T.Uy = abs(T.Uy);");
                                    matlab.Execute("T.Ux = str2double(T.Ux);"); matlab.Execute("T.Ux = abs(T.Ux);");
                                    matlab.Execute("[G, storyGroups] = findgroups(T.Story);");

                                    matlab.Execute("idx_first_y = splitapply(@(x) x(1), (1:height(T))', G);");
                                    matlab.Execute("max_horizontal_y_displacements_per_story = T.Uy(idx_first_y);");//groupsummary(T,'Story','max','Uy');"); 
                                    //matlab.Execute("max_horizontal_y_displacements_per_story = max_horizontal_y_displacements_per_story.max_Uy;"); //T.Uy(idx_first_y);");


                                    matlab.Execute("idx_first_x = splitapply(@(x) x(1), (1:height(T))', G);");
                                    matlab.Execute("max_horizontal_x_displacements_per_story = T.Ux(idx_first_x);");// groupsummary(T,'Story','max','Ux');"); //
                                    //matlab.Execute("max_horizontal_x_displacements_per_story = max_horizontal_x_displacements_per_story.max_Ux;"); //T.Ux(idx_first_x););



                                    matlab.Execute("T_Final.Elevation = str2double(T_Final.Elevation);");
                                    matlab.Execute("max_elevation_values_per_story = groupsummary(T_Final,'Story','max','Elevation');");
                                    matlab.Execute("max_elevation_values_per_story = max_elevation_values_per_story.max_Elevation;");

                                    matlab.Execute("T_Final.Height = str2double(T_Final.Height);");
                                    matlab.Execute("max_height_values_per_story = groupsummary(T_Final,'Story','max','Height');");
                                    matlab.Execute("max_height_values_per_story = max_height_values_per_story.max_Height;");


                                    matlab.Execute("Drifts_x = diff(max_horizontal_x_displacements_per_story);");

                                    matlab.Execute("Drifts_y = diff(max_horizontal_y_displacements_per_story);");

                                    matlab.Execute("Interstory_Drift_Ratios_x = Drifts_x ./ max_height_values_per_story;");
                                    matlab.Execute("Interstory_Drift_Ratios_y = Drifts_y ./ max_height_values_per_story;");

                                    matlab.Execute("Interstory_Drift_Ratio = max([Interstory_Drift_Ratios_x,Interstory_Drift_Ratios_y]);");

                                    matlab.Execute($"{idr_name} = [{idr_name};max(Interstory_Drift_Ratio)];");

                                    #region Adding the Mean Curve

                                    matlab.Execute($"p = polyfit({idr_names_list[0]},{func_name_accelerations_names[0]},1)");

                                    matlab.Execute($"dS1 = polyval(p,DamageS1)");
                                    matlab.Execute($"dS2 = polyval(p,DamageS2)");
                                    matlab.Execute($"dS3 = polyval(p,DamageS3)");
                                    matlab.Execute($"dS4 = polyval(p,DamageS4)");
                                    matlab.Execute($"dS5 = polyval(p,DamageS5)");

                                    matlab.Execute("DS1 = [dS1]");
                                    matlab.Execute("DS2 = [dS2]");
                                    matlab.Execute("DS3 = [dS3]");
                                    matlab.Execute("DS4 = [dS4]");
                                    matlab.Execute("DS5 = [dS5]");

                                    for (int j = 1; j < sf_names_list.Count; j++)
                                    {
                                        string sf = sf_names_list[j];
                                        string idr = idr_names_list[j];
                                        string func_name_accelerations_1 = func_name_accelerations_names[j];
                                        string legendEntry = legendEntries[j];

                                        matlab.Execute($"p = polyfit({idr},{func_name_accelerations_1},1)");

                                        matlab.Execute($"dS1 = polyval(p,DamageS1)");
                                        matlab.Execute($"dS2 = polyval(p,DamageS2)");
                                        matlab.Execute($"dS3 = polyval(p,DamageS3)");
                                        matlab.Execute($"dS4 = polyval(p,DamageS4)");
                                        matlab.Execute($"dS5 = polyval(p,DamageS5)");

                                        matlab.Execute($"DS1 = [DS1;dS1]");
                                        matlab.Execute($"DS2 = [DS2;dS2]");
                                        matlab.Execute($"DS3 = [DS3;dS3]");
                                        matlab.Execute($"DS4 = [DS4;dS4]");
                                        matlab.Execute($"DS5 = [DS5;dS5]");

                                    }

                                    matlab.Execute($"x_values = linspace(0.01,{max_Acceleration / factor},50)");

                                    matlab.Execute("num_pga_levels = 50;");
                                    matlab.Execute("calculated_mean_idr = zeros(num_pga_levels, 1);");
                                    int c = 0;

                                    for (int j = 1; j <= 50; j++)
                                    {
                                        matlab.Execute($"target_pga = x_values({j})");
                                        matlab.Execute($"idr_at_target_pga = zeros({func_name_accelerations_names.Count},1)");
                                        c++;
                                        for (int m = 0; m <= f; m++)
                                        {
                                            matlab.Execute($"[Xunique, ~, ic] = unique({functions[m].Name + "_FuncMax"});");
                                            matlab.Execute($"Ymean = accumarray(ic, {idr_names_list[m]}, [], @mean);");
                                            matlab.Execute($"idr_at_target_pga({m + 1}) = interp1(Xunique,Ymean,target_pga,'linear','extrap');");
                                            
                                            // Printing Progress to Console Window
                                            Console.WriteLine($"\nProsessing.........{c}|{50}:{m}|{f}\nIteration:{iteration}; Story:{storey}; Grid-Y:{g_y}; Grid-X:{g_x}; Function Number:{f}; Function:{func.Name}; Acceleration:{ (SF + i) / factor}; Time Period:{Fundamental_Time_Period}");
                                        }
                                        matlab.Execute("idr_at_target_pga = idr_at_target_pga(idr_at_target_pga>0);");
                                        matlab.Execute($"calculated_mean_idr({j}) = mean(idr_at_target_pga)");
                                    }

                                    // Perform Curve Fitting for Demand Model
                                    // % Fit Mean Demand (mu_D) using Power Law (Eq 5.4.1)
                                    matlab.Execute("p_mean_power = polyfit(log(x_values'), log(calculated_mean_idr),1);");
                                    matlab.Execute("c2_p = p_mean_power(1);");
                                    matlab.Execute("c1_p = exp(p_mean_power(2));");
                                    matlab.Execute("mean_demand_func = @(pga) c1_p * pga.^c2_p;");

                                    matlab.Execute("pga_plot_range_demand = linspace(min(x_values), max(x_values), 50);");
                                    matlab.Execute("fitted_mean_idr = mean_demand_func(pga_plot_range_demand);");

                                    #endregion


                                    matlab.Execute($"n = length({idr_name});");

                                    matlab.Execute($"slope_original = ({func_name_accelerations}(2) - {func_name_accelerations}(1)) ./ ({idr_name}(2) - {idr_name}(1)) ;"); matlab.Execute("slope_original = round(slope_original,4);");
                                    matlab.Execute($"slope_curr = {func_name_accelerations} ./ {idr_name};"); matlab.Execute("slope_curr = slope_curr(end);");

                                    matlab.Execute($"delta_idr_this = {idr_name}(n);"); matlab.Execute("delta_idr_this = round(delta_idr_this,4);");
                                    matlab.Execute($"delta_idr_old = {idr_name}(n-1);"); matlab.Execute("delta_idr_old = round(delta_idr_old,4);");

                                    matlab.Execute("slope_flag = 1;");

                                    matlab.Execute("if delta_idr_old > delta_idr_this, slope_flag = 0; end");
                                    matlab.Execute("if slope_curr > ( 3 *  slope_original), slope_flag = 0; end");

                                    //matlab.Execute("slope_flag = double(~(slope_curr > slope_original)) * (n >= 3) + (n < 3);");
                                    //if (i > 0) 
                                    //{
                                    //    matlab.Execute("meangraph_slope = y_mean / x_values_mean;"); matlab.Execute("meangraph_slope = round(meangraph_slope,4);");
                                    //    matlab.Execute("if slope_curr > ( 5 * meangraph_slope), slope_flag = 0; end");
                                    //}



                                    object slope_flag_temp = null;
                                    matlab.GetWorkspaceData("slope_flag", "base", out slope_flag_temp);

                                    slope_flag = int.Parse(slope_flag_temp.ToString());

                                    if (slope_flag == 1 && slope_flag_first_time == 0)
                                    {
                                        // Get the new column size
                                        // Update the column size in MATLAB
                                        matlab.PutWorkspaceData("SF", "base", scaleFactorLocal[0]);
                                        // Update the acceleration in MATLAB
                                        matlab.Execute($"{SF_name} = [{SF_name}; SF];");
                                        matlab.Execute($"{func_name_accelerations} = ({SF_name}/{factor})");
                                        matlab.Execute($"{idr_name} = [{idr_name}; delta_idr_this]");
                                        matlab.Execute($"{idr_name}(n) = (delta_idr_this + delta_idr_old) / 2;");
                                    }


                                    if (slope_flag == 0)
                                    {
                                        matlab.Execute($"{idr_name}(n) = []");
                                        matlab.Execute($"{func_name_accelerations}(n) = []");
                                        slope_flag_first_time = 0;
                                        break;
                                    }



                                    #endregion

                                    #region Saving and Writing Instructions to Console

                                    matlab.Execute("cd('" + folderPath + "')"); // Change directory to the folder where the model is saved
                                    matlab.Execute("save('etabs_results.mat')");

                                    matlab.Execute("interstory_df = max(Interstory_Drift_Ratio)");

                                    // Get the maximum horizontal displacement from MATLAB

                                    object idrtemp = null;
                                    matlab.GetWorkspaceData("interstory_df", "base", out idrtemp);

                                    double IDR_temp = Convert.ToDouble(idrtemp);
                                    IDR = IDR_temp; // Update the IDR value


                                    Console.WriteLine($"\nFunction: {func.Name} \nScale Factor: {scaleFactorLocal[0]} mm/s2, Accleration Value: {(scaleFactorLocal[0] / factor)} g, Interstory Drift Ratio: {(IDR * 100).ToString()} %");
                                    TimeSpan ts1 = stopwatch.Elapsed;

                                    string elapsedTime1 = String.Format("{0:00}:{1:00}:{2:00}.{3:00}", ts1.Hours, ts1.Minutes, ts1.Seconds, ts1.Milliseconds / 10);

                                    Console.WriteLine("\nTime Since Beginngin: " + elapsedTime1);

                                    matlab.Execute("cd('" + folderPath + "')");
                                    // ... (code to get IDR from MATLAB) ...

                                    // --- 2. APPEND THE RESULTS TO THE CSV FILE HERE ---
                                    string iterationName1 = $"Story_{storey}-BayX_{g_x}-BayY_{g_y}-Func_{func.Name}-SF_{scaleFactorLocal[0]:F2}";
                                    double pga = (scaleFactorLocal[0] / factor) * func.maxValue;

                                    double bayAspectRatio = ((double)g_x + 1.0) / ((double)g_y + 1.0);

                                    string csvLine1 = $"{SNo},{Sub_SNo},{iterationName1},{storyList[storey].Name},{g_x + 1},{g_y + 1},{bayAspectRatio},{colummnSectionRandom.Depth} x {colummnSectionRandom.Width},{beamSectionRandom.Depth} x {beamSectionRandom.Width},{areasectionRandom.Thickness},{materialRandom.FcPrime * 1000},{Math.Round(4700 * Math.Sqrt(materialRandom.FcPrime * 1000))},{colummnSectionRandom.Fy},{beamSectionRandom.Fy},{selectedProfile.ProfileName},{selectedProfile.Layers[0].ShearModulus}, {selectedProfile.Layers[0].PoissonsRatio}, {selectedProfile.Layers[0].FrictionAngle}, {selectedProfile.Layers[0].UnitWeight}, {selectedProfile.Layers[0].ShearWaveVelocity}, {selectedSpring.K_Values[0]}, {selectedSpring.K_Values[2]}, {selectedSpring.K_Values[3]},{func.Name},{scaleFactorLocal[0]},{pga},{Sa},{IDR},{Fundamental_Time_Period},{Frequency},{DS1_IDR},{DS2_IDR},{DS3_IDR},{DS4_IDR},{DS5_IDR},,,,,,,,,,";
                                    File.AppendAllText(csvFilePath, csvLine1 + Environment.NewLine);


                                    matlab.Execute("cd('" + backupFolderPath + "')"); // Change directory to the folder where the model is saved
                                    matlab.Execute("save('etabs_results.mat')");


                                    matlab.Execute("cd('" + folderPath + "')");
                                    #endregion

                                    #region Plotting the Graphs
                                    matlab.Execute("clf(figure1);");      // Clear the figure
                                    matlab.Execute("hold on;");           // Hold it
                                    matlab.Execute("grid on;");           // Turn on grid, labels, etc.
                                    matlab.Execute("ylabel('PGA [g]');");
                                    matlab.Execute("xlabel('Interstory Drift Ratio %');");
                                    matlab.Execute("title('Incremental Dynamic Analysis Curves (Max IDR [%] vs PGA [g])');");
                                    matlab.Execute("xlim([0, (DamageS5)]);");
                                    //matlab.Execute($"y_values = [1 1 1 1 1 1];");
                                    //matlab.Execute($"x_values_mean = [0,DamageS1,DamageS2,DamageS3,DamageS4,DamageS5];");

                                    for (int j = 0; j < sf_names_list.Count; j++)
                                    {
                                        string sf = sf_names_list[j];
                                        string idr = idr_names_list[j];
                                        string func_name_acc = func_name_accelerations_names[j];
                                        // Plot each function's data
                                        matlab.Execute($"plot({idr}, {func_name_acc}, '-o', 'LineWidth', 2, 'Color', colors({j + 1},:), 'DisplayName', '{legendEntries[j]}');");
                                        matlab.Execute("Ax = gca;");
                                        matlab.Execute("xt = Ax.XTick;");
                                        matlab.Execute("xt_percentage = xt * 100;");
                                        matlab.Execute("Ax.XTickLabel = xt_percentage;");
                                        //matlab.Execute($"y_temp = interp1({idr},{func_name_acc},[0,DamageS1,DamageS2,DamageS3,DamageS4,DamageS5], 'linear', 'extrap');");
                                        //matlab.Execute($"y_values = [y_values;y_temp];");

                                    }

                                    //matlab.Execute("x_mean = mean(x_values,1);");
                                    //matlab.Execute("y_mean = mean(y_values(2:end,:),1);");
                                    matlab.Execute("max_y_value = ((DamageS5) / c1_p)^(1/c2_p);");

                                    //matlab.Execute("if y_mean < 0, y_mean = y_mean * -1; end;");

                                    //matlab.Execute($"plot(x_values_mean,y_mean,'--','LineWidth',2,'Color', 'k', 'DisplayName', 'Mean Curve');");

                                    matlab.Execute("plot(fitted_mean_idr, pga_plot_range_demand, '--', 'LineWidth', 2, 'Color', 'k', 'DisplayName', 'Fitted Log Mean Curve');");

                                    matlab.Execute($"plot([DamageS1, DamageS1],[0,max_y_value],'--', 'LineWidth', 1,'Color',[0.00 0.40 0.74],'DisplayName', sprintf('DS1: %.2f %% @ PGA: %.2f g',DamageS1 * 100, (DamageS1 / c1_p)^(1/c2_p)));");
                                    matlab.Execute($"plot([DamageS2, DamageS2],[0,max_y_value],'--', 'LineWidth', 1,'Color',[0.85 0.30 0.10],'DisplayName', sprintf('DS2: %.2f %% @ PGA: %.2f g',DamageS2 * 100, (DamageS2 / c1_p)^(1/c2_p)));");
                                    matlab.Execute($"plot([DamageS3, DamageS3],[0,max_y_value],'--', 'LineWidth', 1,'Color',[0.93 0.60 0.13],'DisplayName', sprintf('DS3: %.2f %% @ PGA: %.2f g',DamageS3 * 100, (DamageS3 / c1_p)^(1/c2_p)));");
                                    matlab.Execute($"plot([DamageS4, DamageS4],[0,max_y_value],'--', 'LineWidth', 1,'Color',[0.49 0.20 0.56],'DisplayName', sprintf('DS4: %.2f %% @ PGA: %.2f g',DamageS4 * 100, (DamageS4 / c1_p)^(1/c2_p)));");
                                    matlab.Execute($"plot([DamageS5, DamageS5],[0,max_y_value],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19],'DisplayName', sprintf('DS5: %.2f %% @ PGA: %.2f g',DamageS5 * 100, (DamageS5 / c1_p)^(1/c2_p)));");


                                    matlab.Execute($"ylim([0, max_y_value]);");
                                    matlab.Execute("legend;");
                                    matlab.Execute("hold off;"); // Release the hold on the figure

                                    

                                    object max_acc_temp = null;
                                    matlab.GetWorkspaceData("max_y_value", "base", out max_acc_temp);

                                    max_Acceleration = double.Parse(max_acc_temp.ToString()) * factor;
                                    increment = max_Acceleration / number_of_step;


                                    string filename_ida_temp = $"{iterationName}_IDA_Curves.png";


                                    string fullpath_ida_temp = Path.Combine(folderPath1, filename_ida_temp);


                                    matlab.Execute("cd('" + folderPath1 + "')"); // Change directory to the folder where the model is saved
                                    matlab.Execute($"save('{iterationName}_results.mat')");

                                    // Save the current figure to the folder

                                    matlab.Execute($"saveas(figure1, '{filename_ida_temp}');");

                                    //matlab.Execute("disp('Running Meta_Regression Analysis... So far')");

                                    //matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO')");
                                    //matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO');");

                                    //matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");
                                    //matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");
                                    


                                    matlab.Execute("cd('" + backupFolderPath + "')"); // Change directory to the folder where the model is saved
                                    matlab.Execute($"save('etabs_results.mat')");

                                    Console.WriteLine($"\nSaved IDA curves to: {fullpath_ida_temp}");

                                    #endregion


                                    firstTimeRun = false;
                                }

                                #endregion

                            }

                            #region Making the Fragility Curves

                            #region Finding Mean and SD Values

                            matlab.Execute($"p = polyfit({idr_names_list[0]},{func_name_accelerations_names[0]},1)");

                            matlab.Execute($"dS1 = polyval(p,DamageS1)");
                            matlab.Execute($"dS2 = polyval(p,DamageS2)");
                            matlab.Execute($"dS3 = polyval(p,DamageS3)");
                            matlab.Execute($"dS4 = polyval(p,DamageS4)");
                            matlab.Execute($"dS5 = polyval(p,DamageS5)");

                            matlab.Execute("DS1 = [dS1]");
                            matlab.Execute("DS2 = [dS2]");
                            matlab.Execute("DS3 = [dS3]");
                            matlab.Execute("DS4 = [dS4]");
                            matlab.Execute("DS5 = [dS5]");

                            for (int j = 1; j < sf_names_list.Count; j++)
                            {
                                string sf = sf_names_list[j];
                                string idr = idr_names_list[j];
                                string func_name_acceleration = func_name_accelerations_names[j];
                                string legendEntry = legendEntries[j];

                                matlab.Execute($"p = polyfit({idr},{func_name_acceleration},1)");

                                matlab.Execute($"dS1 = polyval(p,DamageS1)");
                                matlab.Execute($"dS2 = polyval(p,DamageS2)");
                                matlab.Execute($"dS3 = polyval(p,DamageS3)");
                                matlab.Execute($"dS4 = polyval(p,DamageS4)");
                                matlab.Execute($"dS5 = polyval(p,DamageS5)");

                                matlab.Execute($"DS1 = [DS1;dS1]");
                                matlab.Execute($"DS2 = [DS2;dS2]");
                                matlab.Execute($"DS3 = [DS3;dS3]");
                                matlab.Execute($"DS4 = [DS4;dS4]");
                                matlab.Execute($"DS5 = [DS5;dS5]");

                            }

                            matlab.Execute("x_values = linspace(0.01,(DamageS5 / c1_p)^(1/c2_p) * 2,500)"); // Going over 100 different points to find a good trend for the means

                            // %% 2. CREATE CONTINUOUS DEMAND MODEL (LOADING)
                            matlab.Execute("num_pga_levels = 500;");
                            matlab.Execute("calculated_mean_idr = zeros(num_pga_levels, 1);");
                            matlab.Execute("calculated_log_std = zeros(num_pga_levels, 1);");

                            int p = 0;

                            for (int j = 1; j <= 500; j++)
                            {
                                matlab.Execute($"target_pga = x_values({j})");
                                matlab.Execute($"idr_at_target_pga = zeros({func_name_accelerations_names.Count},1)");
                                p++;
                                for (int m = 1; m <= func_name_accelerations_names.Count; m++)
                                {
                                    matlab.Execute($"idr_at_target_pga({m}) = interp1({func_name_accelerations_names[m - 1]},{idr_names_list[m - 1]},target_pga,'linear','extrap');");

                                    // Printing Progress to Console Window
                                    Console.WriteLine($"\nProsessing.........{p}|{500}:{m}|{func_name_accelerations_names.Count}");
                                }
                                matlab.Execute("idr_at_target_pga = idr_at_target_pga(idr_at_target_pga>0);");
                                matlab.Execute($"calculated_mean_idr({j}) = mean(idr_at_target_pga)");
                                matlab.Execute($"calculated_log_std({j}) = std(idr_at_target_pga)");
                            }

                            // Perform Curve Fitting for Demand Model
                            // % Fit Mean Demand (mu_D) using Power Law (Eq 5.4.1)
                            matlab.Execute("p_mean_power = polyfit(log(x_values'), log(calculated_mean_idr),1);");
                            matlab.Execute("c2_p = p_mean_power(1);");
                            matlab.Execute("c1_p = exp(p_mean_power(2));");
                            matlab.Execute("mean_demand_func = @(pga) c1_p * pga.^c2_p;");

                            

                            matlab.Execute("FittedCurves = figure('Name', 'Fitted Loading Demand Model', 'NumberTitle', 'off');");
                            matlab.Execute("FittedCurves.WindowState = 'maximized'");

                            // Subplot 1: Mean Interstory Drift
                            matlab.Execute("subplot(1, 2, 1);");
                            matlab.Execute("hold on; grid on;");
                            matlab.Execute("plot(calculated_mean_idr, x_values , 'ko', 'MarkerFaceColor', 'k', 'DisplayName', 'Calculated Points');");
                            matlab.Execute("pga_plot_range_demand = linspace(min(x_values), max(x_values), 500);");
                            matlab.Execute("fitted_mean_idr = mean_demand_func(pga_plot_range_demand);");
                            matlab.Execute("plot(fitted_mean_idr, pga_plot_range_demand, 'r-', 'LineWidth', 2, 'DisplayName', 'Fitted Curve (Eq 5.4.1)');");

                            matlab.Execute($"plot([DamageS1, DamageS1],[0,(DamageS1 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.00 0.40 0.74],'DisplayName', sprintf('DS1: %.2f %% @ PGA: %.2f g',DamageS1 * 100, (DamageS1 / c1_p)^(1/c2_p)));");
                            matlab.Execute($"plot([DamageS2, DamageS2],[0,(DamageS2 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.85 0.30 0.10],'DisplayName', sprintf('DS2: %.2f %% @ PGA: %.2f g',DamageS2 * 100, (DamageS2 / c1_p)^(1/c2_p)));");
                            matlab.Execute($"plot([DamageS3, DamageS3],[0,(DamageS3 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.93 0.60 0.13],'DisplayName', sprintf('DS3: %.2f %% @ PGA: %.2f g',DamageS3 * 100, (DamageS3 / c1_p)^(1/c2_p)));");
                            matlab.Execute($"plot([DamageS4, DamageS4],[0,(DamageS4 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.49 0.20 0.56],'DisplayName', sprintf('DS4: %.2f %% @ PGA: %.2f g',DamageS4 * 100, (DamageS4 / c1_p)^(1/c2_p)));");
                            matlab.Execute($"plot([DamageS5, DamageS5],[0,(DamageS5 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19],'DisplayName', sprintf('DS5: %.2f %% @ PGA: %.2f g',DamageS5 * 100, (DamageS5 / c1_p)^(1/c2_p)));");

                            
                            matlab.Execute("xlim([0, DamageS5])");
                            matlab.Execute("ylim([0, ((DamageS5 / c1_p)^(1/c2_p))])");

                            matlab.Execute($"h = plot([0, DamageS1],[(DamageS1 / c1_p)^(1/c2_p),(DamageS1 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.00 0.40 0.74]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([0, DamageS2],[(DamageS2 / c1_p)^(1/c2_p),(DamageS2 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.85 0.30 0.10]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([0, DamageS3],[(DamageS3 / c1_p)^(1/c2_p),(DamageS3 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.93 0.60 0.13]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([0, DamageS4],[(DamageS4 / c1_p)^(1/c2_p),(DamageS4 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.49 0.20 0.56]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([0, DamageS5],[(DamageS5 / c1_p)^(1/c2_p),(DamageS5 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");

                            matlab.Execute("Ax = gca;");
                            matlab.Execute("xt = Ax.XTick;");
                            matlab.Execute("xt_percentage = xt * 100;");
                            matlab.Execute("Ax.XTickLabel = xt_percentage;");
                            matlab.Execute("title('Intensity vs Mean Demand (µ_D)');");
                            matlab.Execute("xlabel('Mean Interstory Drift Ratio');");
                            matlab.Execute("ylabel('PGA (g)');");
                            matlab.Execute("legend('show', 'Location', 'northwest');");

                            matlab.Execute("hold off;");

                            // Subplot 2: Logarithmic Standard Deviation
                            matlab.Execute("x_values = linspace(0.01,(DamageS5 / c1_p)^(1/c2_p),500)"); // Going over 100 different points to find a good trend for the means
                            // % Fit Logarithmic Standard Deviation (beta_D) using Quadratic (Eq 5.4.3)
                            matlab.Execute("p_std_quad = polyfit(x_values', calculated_log_std, 2);");
                            matlab.Execute("c6 = p_std_quad(1);");
                            matlab.Execute("c5 = p_std_quad(2);");
                            matlab.Execute("c4 = p_std_quad(3);");
                            matlab.Execute("std_demand_func = @(pga) c4 + c5*pga + c6*pga.^2;");

                            matlab.Execute("subplot(1, 2, 2);");
                            matlab.Execute("hold on; grid on;");
                            matlab.Execute("plot(calculated_log_std, x_values,  'ko', 'MarkerFaceColor', 'k', 'DisplayName', 'Calculated Points');");
                            matlab.Execute("pga_plot_range_demand = linspace(min(x_values), max(x_values), 500);");
                            matlab.Execute("fitted_log_std = std_demand_func(pga_plot_range_demand);");
                            matlab.Execute("plot(fitted_log_std,pga_plot_range_demand, 'r-', 'LineWidth', 2, 'DisplayName', 'Quadratic Fit (Eq 5.4.3)');");


                            matlab.Execute($"h = plot([0, std_demand_func((DamageS1 / c1_p)^(1/c2_p))],[(DamageS1 / c1_p)^(1/c2_p),(DamageS1 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.00 0.40 0.74]); h.Annotation.LegendInformation.IconDisplayStyle='off'");
                            matlab.Execute($"h = plot([0, std_demand_func((DamageS2 / c1_p)^(1/c2_p))],[(DamageS2 / c1_p)^(1/c2_p),(DamageS2 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.85 0.30 0.10]); h.Annotation.LegendInformation.IconDisplayStyle='off'");
                            matlab.Execute($"h = plot([0, std_demand_func((DamageS3 / c1_p)^(1/c2_p))],[(DamageS3 / c1_p)^(1/c2_p),(DamageS3 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.93 0.60 0.13]); h.Annotation.LegendInformation.IconDisplayStyle='off'");
                            matlab.Execute($"h = plot([0, std_demand_func((DamageS4 / c1_p)^(1/c2_p))],[(DamageS4 / c1_p)^(1/c2_p),(DamageS4 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.49 0.20 0.56]); h.Annotation.LegendInformation.IconDisplayStyle='off'");
                            matlab.Execute($"h = plot([0, std_demand_func((DamageS5 / c1_p)^(1/c2_p))],[(DamageS5 / c1_p)^(1/c2_p),(DamageS5 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off'");


                            matlab.Execute($"h = plot([std_demand_func((DamageS1 / c1_p)^(1/c2_p)), std_demand_func((DamageS1 / c1_p)^(1/c2_p))],[0,(DamageS1 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([std_demand_func((DamageS2 / c1_p)^(1/c2_p)), std_demand_func((DamageS2 / c1_p)^(1/c2_p))],[0,(DamageS2 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([std_demand_func((DamageS3 / c1_p)^(1/c2_p)), std_demand_func((DamageS3 / c1_p)^(1/c2_p))],[0,(DamageS3 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([std_demand_func((DamageS4 / c1_p)^(1/c2_p)), std_demand_func((DamageS4 / c1_p)^(1/c2_p))],[0,(DamageS4 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");
                            matlab.Execute($"h = plot([std_demand_func((DamageS5 / c1_p)^(1/c2_p)), std_demand_func((DamageS5 / c1_p)^(1/c2_p))],[0,(DamageS5 / c1_p)^(1/c2_p)],'--', 'LineWidth', 1,'Color',[0.47 0.60 0.19]); h.Annotation.LegendInformation.IconDisplayStyle='off';");

                            matlab.Execute("title('Intensity vs. Logarithmic Standard Deviation (ß_D)');");
                            matlab.Execute("ylabel('PGA (g)');");
                            matlab.Execute("xlabel('Logarithmic Standard Deviation (ß)');");
                            matlab.Execute("legend('show', 'Location', 'best');");
                            matlab.Execute("ylim([0, ((DamageS5 / c1_p)^(1/c2_p))])");
                            matlab.Execute("xlim([0 std_demand_func((DamageS5 / c1_p)^(1/c2_p))])");
                            matlab.Execute("hold off;");

                            // Defining Structural Capacity Model
                            matlab.Execute($"capacity_median_idr = [{DS1_IDR},{DS2_IDR},{DS3_IDR},{DS4_IDR},{DS5_IDR}]");
                            matlab.Execute("ds_labels = {'DS1: Slight', 'DS2: Moderate', 'DS3: Extensive', 'DS4: Complete', 'DS5: Collapse'}");
                            matlab.Execute("num_damage_states = length(capacity_median_idr);");
                            matlab.Execute("capacity_log_std = 0.35;"); // Assuming Constant structural response standard deviation i.e. materials etc causing it
                            #endregion

                            #region Calculating Fragility Curve Parameters

                            matlab.Execute("fragility_median_pga = zeros(1,num_damage_states);");
                            matlab.Execute("fragility_total_log_std = zeros(1,num_damage_states);");

                            for (int i = 1; i <= 5; i++)
                            {
                                matlab.Execute($"current_median_capacity = capacity_median_idr({i})");
                                matlab.Execute("median_pga = (current_median_capacity / c1_p) ^ (1 / c2_p);");
                                matlab.Execute($"fragility_median_pga({i}) = median_pga;");
                                matlab.Execute($"DS{i}_mean = median_pga");
                                matlab.Execute("demand_std_at_median = std_demand_func(median_pga);");
                                matlab.Execute("total_std = sqrt(demand_std_at_median^2 + capacity_log_std^2);");
                                matlab.Execute($"fragility_total_log_std({i}) = total_std;");
                                matlab.Execute($"DS{i}_sd = total_std");
                            }

                            // Plotting the Fragility Curves
                            // making a new figure named fragility curves
                            matlab.Execute("FragilityCurves = figure('Name', 'Fragility Curves Plot', 'NumberTitle', 'off');");
                            matlab.Execute("FragilityCurves.WindowState = 'maximized';");
                            matlab.Execute("hold on");
                            matlab.Execute("grid on;");           // Turn on grid, labels, etc.
                            matlab.Execute("xlabel('Peak Ground Acceleration (PGA) [g]');");
                            matlab.Execute("ylabel('Probability of Exceedence');");
                            matlab.Execute("title('Seismic Fragility Curves');");

                            for (int i = 1; i <= 5; i++)
                            {
                                matlab.Execute($"prob_exceedance = logncdf(x_values, log(fragility_median_pga({i})), fragility_total_log_std({i}));");
                                matlab.Execute($"i = {i}");
                                matlab.Execute("legend_label = sprintf('%s [%.2f%%] (µ = %.2fg, ß = %.3f)', ds_labels{i}, DamageStates(i), fragility_median_pga(i), fragility_total_log_std(i));");
                                matlab.Execute($"plot(x_values, prob_exceedance, '-', 'LineWidth', 2, 'DisplayName', legend_label);");
                            }

                            matlab.Execute("legend('show', 'Location', 'southeast');");
                            matlab.Execute("ylim([0, 1]);");
                            matlab.Execute("xlim([0, (DamageS5 / c1_p)^(1/c2_p)]);"); // Set x-axis limits to 0-10g
                            matlab.Execute("hold off;");


                            // Get the mean and standard deviation of the DS1, DS2, DS3, DS4, and DS5
                            object ds1_mean = null; matlab.GetWorkspaceData("DS1_mean", "base", out ds1_mean);
                            object ds1_sd = null; matlab.GetWorkspaceData("DS1_sd", "base", out ds1_sd);
                            object ds2_mean = null; matlab.GetWorkspaceData("DS2_mean", "base", out ds2_mean);
                            object ds2_sd = null; matlab.GetWorkspaceData("DS2_sd", "base", out ds2_sd);
                            object ds3_mean = null; matlab.GetWorkspaceData("DS3_mean", "base", out ds3_mean);
                            object ds3_sd = null; matlab.GetWorkspaceData("DS3_sd", "base", out ds3_sd);
                            object ds4_mean = null; matlab.GetWorkspaceData("DS4_mean", "base", out ds4_mean);
                            object ds4_sd = null; matlab.GetWorkspaceData("DS4_sd", "base", out ds4_sd);
                            object ds5_mean = null; matlab.GetWorkspaceData("DS5_mean", "base", out ds5_mean);
                            object ds5_sd = null; matlab.GetWorkspaceData("DS5_sd", "base", out ds5_sd);


                            #endregion


                            matlab.Execute("cd('" + folderPath + "')"); // Change directory to the folder where the model is saved
                            matlab.Execute("save('etabs_results.mat')");

                            double bayAspectRatio_FC = ((g_x + 1) / (g_y + 1));
                            string csvLine = $"{SNo},{Sub_SNo},Story_{storey}-BayX_{g_x}-BayY_{g_y}_Fragility_Curves_Data,{storyList[storey].Name},{g_x + 1},{g_y + 1},{bayAspectRatio_FC},{colummnSectionRandom.Depth} x {colummnSectionRandom.Width},{beamSectionRandom.Depth} x {beamSectionRandom.Width},{areasectionRandom.Thickness},{materialRandom.FcPrime * 1000},{Math.Round(4700 * Math.Sqrt(materialRandom.FcPrime * 1000))},{colummnSectionRandom.Fy},{beamSectionRandom.Fy},{selectedProfile.ProfileName},,,,,,,,,,,,,,{Fundamental_Time_Period},{Frequency},{DS1_IDR},{DS2_IDR},{DS3_IDR},{DS4_IDR},{DS5_IDR},{ds1_mean},{ds1_sd},{ds2_mean},{ds2_sd},{ds3_mean},{ds3_sd},{ds4_mean},{ds4_sd},{ds5_mean},{ds5_sd}";
                            File.AppendAllText(csvFilePath, csvLine + Environment.NewLine);
                            Sub_SNo = 0;

                            #endregion


                            #endregion

                            #region Saving this Iteration

                            // Saving the results for this specific run 
                            // Create a name for this iteration
                            
                            string filename_ida = $"{iterationName}_IDA_Curves.png";
                            string filename_fragility = $"{iterationName}_Fragility_Curves.png";
                            string filename_fitted = $"{iterationName}_Fitted_Curves.png";
                            


                            string fullpath_ida = Path.Combine(folderPath1, filename_ida);
                            string fullpath_fragility = Path.Combine(folderPath1, filename_fragility);
                            string fullpath_fitted = Path.Combine(folderPath1, filename_fitted);

                            matlab.Execute("PushOverCurve.Visible = 'on';");

                            matlab.Execute("cd('" + folderPath1 + "')"); // Change directory to the folder where the model is saved
                            matlab.Execute($"save('{iterationName}_results.mat')");

                            // Save the current figure to the folder

                            
                            



                            matlab.Execute($"saveas(figure1, '{filename_ida}');");
                            matlab.Execute($"saveas(FragilityCurves, '{filename_fragility}');");
                            matlab.Execute($"saveas(FittedCurves, '{filename_fitted}');");
                            matlab.Execute($"saveas(PushOverCurve, '{fullpath_pushover}');");


                            Console.WriteLine($"\nSaved IDA curves to: {fullpath_ida}");
                            Console.WriteLine($"Saved Fragility curves to: {fullpath_fragility}");
                            Console.WriteLine($"Saved Fitted curves to: {fullpath_fitted}");
                            Console.WriteLine($"Saved PushOverCurve to: {fullpath_pushover}");


                            matlab.Execute("disp('Running Meta_Regression Analysis... For this Run')");

                            matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO')");
                            matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO');");

                            matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");
                            matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");




                            matlab.Execute("cd('" + backupFolderPath + "')"); // Change directory to the folder where the model is saved
                            matlab.Execute($"save('etabs_results.mat')");

                            Console.WriteLine($"\n{iterationName} completed and saved successfully.");



                            #endregion

                            matlab.Execute("close all;"); // Close all figures to avoid cluttering the MATLAB workspace
                            SNo++;
                            iteration++;

                        }

                        g_y_starter = 0;
                    }
                    g_x_starter = 0;
                }

                #endregion

                #region Cleaning Up

                Console.WriteLine("\nAll commands executed successfully.");
                // 3. Get the elapsed time as a TimeSpan object
                TimeSpan ts = stopwatch.Elapsed;

                // 4. Format the TimeSpan for a readable output
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);

                Console.WriteLine("\nThis Runtime: " + elapsedTime);

                Console.WriteLine("\nRunning Meta_Regression Analysis");

                matlab.Execute("cd('" + folderPath1 + "')"); // Change directory to the folder where the model is saved
                matlab.Execute("save('etabs_results.mat')");

                matlab.Execute("disp('Running Meta_Regression Analysis...')");
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','mean','meta_regression_generalized_mean');");
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','mean','meta_regression_generalized_mean');");
                

                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','sd','meta_regression_generalized_sd');");
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','sd','meta_regression_generalized_sd');");
                
                
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO')");
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'DS1','DS2','DS3','DS4','DS5'},'SNo','PO','meta_regression_generalized_PO');");

                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'ConcreteModulus'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");
                matlab.Execute("meta_regression_generalized('ETABS_Data.csv',{'Story','BayX','BayY','ColumnSize','BeamSize','SlabThickness','ConcreteStrength'},{'MaxIDR'},'SNo','g','meta_regression_generalized_MaxIDR');");
                

                // saving all opened up matlab plots to png



                Console.WriteLine("\nETABS model created and saved successfully.\n");
                stopwatch.Stop();
                Console.WriteLine("Press any key to close the application...");
                Console.ReadLine();

                // Closing the SAP2000 model
                etabsObject.ApplicationExit(true); // Close the ETABS application after saving changes
                // Closing MATLAB
                matlab.Execute("close all;"); // Close all figures in MATLAB
                matlab.Quit();


                #endregion

                #endregion
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("\n---- Stack Trace-----");
                Console.WriteLine(ex.StackTrace);
                etabsObject.ApplicationExit(false); // Unhide the ETABS application if an error occurs
                
            }
        }

        private static void Importing_Frame_Objects(cSapModel sapModel, List<FrameSectionInfo> frameSectionList, List<FrameObject> frameObjectList)
        {
            // --- Frame Objects ---
            int frameObjectCount = 0;
            string[] frameObjectNames = null;
            sapModel.FrameObj.GetNameList(ref frameObjectCount, ref frameObjectNames);


            if (frameObjectCount > 0)
            {
                foreach (var name in frameObjectNames)
                {
                    string sectionName = "", materialName = "", story = "", label = "", lineType = "", guid = "", pointI = "", pointJ = "";
                    //double length = 0;
                    string notes = "";
                    //int dummy = 0;
                    string sAuto = ""; // Auto section assignment, if any
                    eFrameDesignOrientation DesignType = 0; // Design orientation type, if needed


                    FrameObject frameObject = new FrameObject();

                    // Get properties of the frame object
                    frameObject.Name = name;


                    sapModel.FrameObj.GetSection(name, ref sectionName, ref sAuto);
                    sapModel.FrameObj.GetMaterialOverwrite(name, ref materialName);
                    sapModel.FrameObj.GetDesignOrientation(name, ref DesignType); // optional
                    sapModel.FrameObj.GetPoints(name, ref pointI, ref pointJ);

                    // Get Coordinates
                    double xi = 0, yi = 0, zi = 0;
                    double xj = 0, yj = 0, zj = 0;
                    sapModel.PointObj.GetCoordCartesian(pointI, ref xi, ref yi, ref zi);
                    sapModel.PointObj.GetCoordCartesian(pointJ, ref xj, ref yj, ref zj);

                    // Infer stroy using class method

                    int numStories = 0;
                    string[] storyNames = null;
                    double[] storyElevations = null;
                    double[] storyHeights = null;
                    bool[] isMasterStory = null;
                    string[] similarToStory = null;
                    bool[] spliceAbove = null;
                    double[] spliceHeight = null;

                    // Get all story properties at once
                    sapModel.Story.GetStories(ref numStories, ref storyNames, ref storyElevations,
                                                ref storyHeights, ref isMasterStory, ref similarToStory,
                                                ref spliceAbove, ref spliceHeight);

                    // Get the story names and elevations
                    string storyI = FrameObject.GetStoryFromZ(zi, storyNames, storyElevations);
                    string storyJ = FrameObject.GetStoryFromZ(zj, storyNames, storyElevations);
                    story = (storyI == storyJ) ? storyI : $"{storyI}/{storyJ}";


                    sapModel.FrameObj.GetLabelFromName(name, ref label, ref story);
                    sapModel.FrameObj.GetGUID(name, ref guid);
                    //sapModel.FrameObj.Getl(name, ref notes);

                    MaterialInfo material = new MaterialInfo();
                    material.Name = materialName;

                    var frame = new FrameObject
                    {
                        Name = name,
                        sapModel = sapModel,
                        SectionName = frameSectionList.FirstOrDefault(s => s.Name == sectionName),
                        MaterialName = material,
                        GUID = guid,
                        Notes = notes,
                        Story = story,
                        LineType = lineType,
                        Length = Math.Sqrt(Math.Pow(xj - xi, 2) + Math.Pow(yj - yi, 2) + Math.Pow(zj - zi, 2)),
                        PointNames = new string[] { pointI, pointJ },
                    };

                    frameObjectList.Add(frame);
                }
            }
        }
    }
}
