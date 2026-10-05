using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using Jint;

namespace SupportFins.SolidWorks
{
    [Guid("B6F3E6C1-2856-4BD3-A5B2-F405E72E9B39")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class SupportFinsAddin : ISwAddin
    {
        private SldWorks _swApp;
        private int _addinCookie;
        private ICommandManager _cmdMgr;

        private const int CommandGroupId = 1001;
        private const int CommandId = 1;

        #region Lifecycle

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            _swApp = (SldWorks)ThisSW;
            _addinCookie = cookie;

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            _swApp.SetAddinCallbackInfo2(0, this, _addinCookie);

            AddCommandManagerUI();
            return true;
        }

        public bool DisconnectFromSW()
        {
            RemoveCommandManagerUI();
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;

            if (_swApp != null)
            {
                Marshal.ReleaseComObject(_swApp);
                _swApp = null;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string assemblyPath = Path.Combine(dllDir, assemblyName);
                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
            }
            catch { }
            return null;
        }

        #endregion

        #region UI

        private void AddCommandManagerUI()
        {
            _cmdMgr = _swApp.GetCommandManager(_addinCookie);
            if (_cmdMgr == null) return;

            int cmdGroupErr = 0;
            ICommandGroup cmdGroup = _cmdMgr.CreateCommandGroup2(
                CommandGroupId,
                "Support Fins",
                "Support Fins für 3D-Druck nach Matthew Trahan",
                "Support Fins",
                -1,
                false,
                ref cmdGroupErr);

            if (cmdGroup != null)
            {
                cmdGroup.LargeIconList = "";
                cmdGroup.SmallIconList = "";
                cmdGroup.LargeMainIcon = "";
                cmdGroup.SmallMainIcon = "";

                int menuToolbarOption = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);

                int cmdIndex = cmdGroup.AddCommandItem2(
                    "Support Fins erzeugen",
                    -1,
                    "Erzeugt 3D-Druck-Stützfinnen mit Zacken und Sollbruchstellen",
                    "Support Fins erzeugen",
                    0,
                    "OnGenerateFins",
                    "EnableGenerateFins",
                    CommandId,
                    menuToolbarOption);

                cmdGroup.HasToolbar = true;
                cmdGroup.HasMenu = true;
                cmdGroup.Activate();

                int[] docTypes = new int[] { (int)swDocumentTypes_e.swDocPART };
                foreach (int docType in docTypes)
                {
                    CommandTab cmdTab = (CommandTab)_cmdMgr.GetCommandTab(docType, "Support Fins");
                    if (cmdTab != null)
                    {
                        _cmdMgr.RemoveCommandTab(cmdTab);
                    }

                    cmdTab = (CommandTab)_cmdMgr.AddCommandTab(docType, "Support Fins");
                    if (cmdTab != null)
                    {
                        CommandTabBox cmdBox = (CommandTabBox)cmdTab.AddCommandTabBox();
                        int[] cmdIDs = new int[] { cmdGroup.get_CommandID(cmdIndex) };
                        int[] textTypes = new int[] { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow };
                        cmdBox.AddCommands(cmdIDs, textTypes);
                    }
                }
            }
        }

        private void RemoveCommandManagerUI()
        {
            try
            {
                if (_cmdMgr != null)
                {
                    _cmdMgr.RemoveCommandGroup2(CommandGroupId, true);
                    _cmdMgr = null;
                }
            }
            catch { }
        }

        #endregion

        #region Callbacks

        public void OnGenerateFins()
        {
            try
            {
                GenerateFins();
            }
            catch (TimeoutException)
            {
                _swApp.SendMsgToUser2(
                    "Berechnung wegen Zeitüberschreitung abgebrochen.\n\n" +
                    "Tipp: Prüfe die Ausrichtung des Modells oder reduziere die Dreiecksauflösung unter Dokumenteigenschaften > Bildqualität.",
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
            }
            catch (Exception ex)
            {
                _swApp.SendMsgToUser2("Fehler bei der Finnen-Berechnung:\n" + ex.Message,
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        public int EnableGenerateFins()
        {
            if (_swApp == null) return 0;
            ModelDoc2 model = _swApp.ActiveDoc as ModelDoc2;
            return (model != null && model.GetType() == (int)swDocumentTypes_e.swDocPART) ? 1 : 0;
        }

        #endregion

        #region Kern-Berechnung

        private void GenerateFins()
        {
            ModelDoc2 model = _swApp.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                _swApp.SendMsgToUser2("Bitte öffnen Sie ein Teil-Dokument (Part).",
                    (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            PartDoc part = (PartDoc)model;

            object[] allBodies = (object[])part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            if (allBodies == null || allBodies.Length == 0)
            {
                _swApp.SendMsgToUser2("Keine Festkörper im Teil gefunden.",
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            // 1. FILTER: Nur Originalkörper erfassen (alte Finnen ignorieren)
            List<IBody2> targetBodies = new List<IBody2>();
            foreach (IBody2 b in allBodies)
            {
                string bName = b.Name ?? "";
                if (bName.StartsWith("Importiert", StringComparison.OrdinalIgnoreCase) ||
                    bName.StartsWith("Imported", StringComparison.OrdinalIgnoreCase) ||
                    bName.StartsWith("Support Fins", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                targetBodies.Add(b);
            }

            if (targetBodies.Count == 0)
            {
                targetBodies.Add((IBody2)allBodies[0]);
            }

            // 2. Roh-Dreiecke in Metern auslesen (true = NoConversion, liefert immer Meter)
            List<double> rawCoordsMeters = new List<double>();
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (IBody2 body in targetBodies)
            {
                object[] faces = (object[])body.GetFaces();
                if (faces == null) continue;

                foreach (IFace2 face in faces)
                {
                    float[] tess = (float[])face.GetTessTriangles(true);
                    if (tess != null && tess.Length > 0)
                    {
                        for (int i = 0; i < tess.Length; i += 3)
                        {
                            double x = tess[i];
                            double y = tess[i + 1];
                            double z = tess[i + 2];

                            rawCoordsMeters.Add(x);
                            rawCoordsMeters.Add(y);
                            rawCoordsMeters.Add(z);

                            if (x < minX) minX = x; if (x > maxX) maxX = x;
                            if (y < minY) minY = y; if (y > maxY) maxY = y;
                            if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                        }
                    }
                }
            }

            if (rawCoordsMeters.Count == 0)
            {
                _swApp.SendMsgToUser2("Konnte keine Dreiecksdaten aus dem Körper lesen.",
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            // 3. Ausrichtung bestimmen: Ist Z (Ebene vorne) oder Y (Ebene oben) die dünnste Achse?
            double dimY = maxY - minY;
            double dimZ = maxZ - minZ;
            bool isFrontPlane = (dimZ < dimY * 0.6);

            // Umrechnung in Millimeter und Anpassung an Engine Z-Up
            List<double> positionsMm = new List<double>(rawCoordsMeters.Count);
            for (int i = 0; i < rawCoordsMeters.Count; i += 3)
            {
                double swX = rawCoordsMeters[i] * 1000.0;
                double swY = rawCoordsMeters[i + 1] * 1000.0;
                double swZ = rawCoordsMeters[i + 2] * 1000.0;

                if (isFrontPlane)
                {
                    // Ebene vorne: Z zeigt nach oben
                    positionsMm.Add(swX);
                    positionsMm.Add(swY);
                    positionsMm.Add(swZ);
                }
                else
                {
                    // Ebene oben: Y zeigt nach oben (Transformation in Engine Z-Up)
                    positionsMm.Add(swX);
                    positionsMm.Add(-swZ);
                    positionsMm.Add(swY);
                }
            }

            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string jsPath = Path.Combine(dllDir, "supportfins-engine.js");
            if (!File.Exists(jsPath))
            {
                jsPath = Path.Combine(dllDir, "engine", "supportfins-engine.js");
            }

            if (!File.Exists(jsPath))
            {
                _swApp.SendMsgToUser2("Datei 'supportfins-engine.js' nicht gefunden!\nPfad: " + jsPath,
                    (int)swMessageBoxIcon_e.swMbStop, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            string engineCode = File.ReadAllText(jsPath);

            // 4. Jint mit 180s Sicherheits-Timeout ausführen
            var jsEngine = new Engine(cfg => cfg.TimeoutInterval(TimeSpan.FromSeconds(180)));
            jsEngine.Execute(engineCode);

            jsEngine.SetValue("inputPositions", positionsMm.ToArray());
            jsEngine.SetValue("layerHeight", 0.2);

            jsEngine.Execute(@"
                var result = SupportFinsEngine.computeFins(inputPositions, {
                    layerHeight: layerHeight,
                    tines: true,
                    tineDensity: 1.0,
                    coverage: 1.0,
                    threshold: 45,
                    bedPad: true,
                    padStyle: 'sure'
                });
            ");

            var resultObj = jsEngine.GetValue("result").AsObject();
            var triVal = resultObj.Get("triangles");
            var offsetObj = resultObj.Get("offset").AsObject();

            var statsObj = resultObj.Get("stats").AsObject();
            int tinesCount = (int)statsObj.Get("tines").AsNumber();
            int bracesCount = (int)statsObj.Get("braces").AsNumber();

            double offX = offsetObj.Get("x").AsNumber();
            double offY = offsetObj.Get("y").AsNumber();
            double offZ = offsetObj.Get("z").AsNumber();

            int triCount = (int)triVal.Get("length").AsNumber();
            if (triCount == 0)
            {
                string planeText = isFrontPlane ? "Ebene vorne (Z-Achse)" : "Ebene oben (Y-Achse)";
                _swApp.SendMsgToUser2($"Keine Finnen erforderlich!\nBezugsebene: {planeText}\nDas Modell liegt ohne ungestützte Überhänge auf dem Druckbett.",
                    (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }

            // 5. STL schreiben
            string tempStl = Path.Combine(Path.GetTempPath(), $"SupportFins_{Guid.NewGuid():N}.stl");
            WriteBinaryStl(tempStl, triVal, offX, offY, offZ, triCount, isFrontPlane);

            // 6. Feature in SolidWorks importieren
            try
            {
                int importErrors = 0;
                Feature importedFeat = (Feature)part.InsertImportedFeature(tempStl, out importErrors);
                if (importedFeat != null)
                {
                    importedFeat.Name = "Support Fins (Trahan Engine)";
                    model.EditRebuild3();
                    string planeUsed = isFrontPlane ? "Ebene vorne (Z)" : "Ebene oben (Y)";
                    _swApp.SendMsgToUser2(
                        $"Support-Finnen erfolgreich generiert!\n\n" +
                        $"• Bezugsebene: {planeUsed}\n" +
                        $"• Berechnete Dreiecke: {triCount / 9:N0}\n" +
                        $"• Generierte Zacken (Tines): {tinesCount}\n" +
                        $"• Gezackte Finnenwände: {bracesCount}\n\n" +
                        $"Hinweis: Die Zacken halten 0,2 mm Sollbruch-Abstand zum Bauteil.",
                        (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                }
                else
                {
                    _swApp.SendMsgToUser2($"Finnen wurden berechnet, Import schlug jedoch fehl (Fehlercode: {importErrors}).",
                        (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
                }
            }
            finally
            {
                try { File.Delete(tempStl); } catch { }
            }
        }

        private static void WriteBinaryStl(string filePath, Jint.Native.JsValue triVal, double offX, double offY, double offZ, int length, bool isFrontPlane)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                byte[] header = new byte[80];
                System.Text.Encoding.ASCII.GetBytes("SupportFins Matthew Trahan Engine").CopyTo(header, 0);
                bw.Write(header);

                uint numTriangles = (uint)(length / 9);
                bw.Write(numTriangles);

                for (int i = 0; i < length; i += 9)
                {
                    double rx1 = triVal.Get((uint)i).AsNumber() - offX;
                    double ry1 = triVal.Get((uint)(i + 1)).AsNumber() - offY;
                    double rz1 = triVal.Get((uint)(i + 2)).AsNumber() - offZ;

                    double rx2 = triVal.Get((uint)(i + 3)).AsNumber() - offX;
                    double ry2 = triVal.Get((uint)(i + 4)).AsNumber() - offY;
                    double rz2 = triVal.Get((uint)(i + 5)).AsNumber() - offZ;

                    double rx3 = triVal.Get((uint)(i + 6)).AsNumber() - offX;
                    double ry3 = triVal.Get((uint)(i + 7)).AsNumber() - offY;
                    double rz3 = triVal.Get((uint)(i + 8)).AsNumber() - offZ;

                    float x1, y1, z1, x2, y2, z2, x3, y3, z3;

                    if (isFrontPlane)
                    {
                        x1 = (float)rx1; y1 = (float)ry1; z1 = (float)rz1;
                        x2 = (float)rx2; y2 = (float)ry2; z2 = (float)rz2;
                        x3 = (float)rx3; y3 = (float)ry3; z3 = (float)rz3;
                    }
                    else
                    {
                        x1 = (float)rx1; y1 = (float)rz1; z1 = (float)(-ry1);
                        x2 = (float)rx2; y2 = (float)rz2; z2 = (float)(-ry2);
                        x3 = (float)rx3; y3 = (float)rz3; z3 = (float)(-ry3);
                    }

                    float ax = x2 - x1, ay = y2 - y1, az = z2 - z1;
                    float bx = x3 - x1, by = y3 - y1, bz = z3 - z1;
                    float nx = ay * bz - az * by;
                    float ny = az * bx - ax * bz;
                    float nz = ax * by - ay * bx;
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-6f) { nx /= len; ny /= len; nz /= len; } else { nx = 0; ny = 1; nz = 0; }

                    bw.Write(nx);
                    bw.Write(ny);
                    bw.Write(nz);

                    bw.Write(x1);
                    bw.Write(y1);
                    bw.Write(z1);

                    bw.Write(x2);
                    bw.Write(y2);
                    bw.Write(z2);

                    bw.Write(x3);
                    bw.Write(y3);
                    bw.Write(z3);

                    bw.Write((ushort)0);
                }
            }
        }

        #endregion

        #region COM Registrierung

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                using (RegistryKey hkLm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey rk = hkLm.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}"))
                {
                    rk.SetValue(null, 1);
                    rk.SetValue("Title", "Support Fins");
                    rk.SetValue("Description", "Generiert Stützfinnen für 3D-Druck nach Matthew Trahan");
                }
            }
            catch
            {
                try
                {
                    using (RegistryKey hkCu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    using (RegistryKey rk = hkCu.CreateSubKey($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}"))
                    {
                        rk.SetValue(null, 1);
                        rk.SetValue("Title", "Support Fins");
                        rk.SetValue("Description", "Generiert Stützfinnen für 3D-Druck nach Matthew Trahan");
                    }
                }
                catch { }
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                using (RegistryKey hkLm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    hkLm.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}", false);
                }
            }
            catch { }

            try
            {
                using (RegistryKey hkCu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    hkCu.DeleteSubKeyTree($@"SOFTWARE\SolidWorks\Addins\{{{t.GUID}}}", false);
                }
            }
            catch { }
        }

        #endregion
    }
}