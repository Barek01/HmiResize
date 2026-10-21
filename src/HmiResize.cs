// HmiResize - Adapte les vues d'un IHM WinCC Unified à une nouvelle résolution d'écran
// via TIA Portal Openness (se connecte au TIA Portal déjà ouvert).
//
// Compilation : voir build.bat (csc.exe du .NET Framework 4.8, C# 5).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;

namespace HmiResize
{
    // ------------------------------------------------------------------------------------------
    // Options de la ligne de commande
    // ------------------------------------------------------------------------------------------
    class Options
    {
        public int FromW, FromH;            // 0 = détection automatique
        public int ToW, ToH;
        public string Mode = "stretch";     // stretch | uniform
        public string Font = "auto";        // auto | none | <facteur>
        public bool ScaleLines;             // épaisseurs de traits / bordures
        public bool ScaleParts = true;      // polices, colonnes, barres d'outils des contrôles
        public bool ScaleScripts = true;    // coordonnées en dur des OpenScreenInPopup
        public bool DryRun;
        public bool Save;
        public bool ListOnly;
        public string Dump;                 // vues dont on affiche tous les objets et attributs
        public bool CompileOnly;
        public bool SaveOnly;
        public string Device;               // filtre nom de l'IHM (joker * ?)
        public string Screens;              // filtre noms des vues, séparés par ';' (joker * ?)
        public string Exclude;              // vues à exclure
        public int Pid;
        public string LogFile;

        // Résolutions connues (Unified Basic / Comfort)
        public static readonly Dictionary<string, int[]> Presets = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "MTP400",  new[] { 480, 272 } },
            { "MTP700",  new[] { 800, 480 } },
            { "MTP1000", new[] { 1280, 800 } },
            { "MTP1200", new[] { 1280, 800 } },
            { "MTP1500", new[] { 1920, 1080 } },
            { "MTP1900", new[] { 1920, 1080 } },
            { "MTP2200", new[] { 1920, 1080 } },
        };

        public static bool TryParseResolution(string s, out int w, out int h)
        {
            w = h = 0;
            int[] p;
            if (Presets.TryGetValue(s.Trim(), out p)) { w = p[0]; h = p[1]; return true; }
            string[] parts = s.ToLowerInvariant().Split('x', '*', ',');
            return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h) && w > 0 && h > 0;
        }

        public static Options Parse(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                Func<string> next = () =>
                {
                    if (i + 1 >= args.Length) throw new ArgumentException("Valeur manquante après " + args[i]);
                    return args[++i];
                };
                switch (a)
                {
                    case "--from":
                        string f = next();
                        if (!f.Equals("auto", StringComparison.OrdinalIgnoreCase) && !TryParseResolution(f, out o.FromW, out o.FromH))
                            throw new ArgumentException("Résolution source invalide : " + f);
                        break;
                    case "--to":
                        string t = next();
                        if (!TryParseResolution(t, out o.ToW, out o.ToH)) throw new ArgumentException("Résolution cible invalide : " + t);
                        break;
                    case "--mode": o.Mode = next().ToLowerInvariant(); break;
                    case "--font": o.Font = next().ToLowerInvariant(); break;
                    case "--lines": o.ScaleLines = true; break;
                    case "--no-parts": o.ScaleParts = false; break;
                    case "--no-scripts": o.ScaleScripts = false; break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--save": o.Save = true; break;
                    case "--list": o.ListOnly = true; break;
                    case "--dump": o.Dump = next(); break;
                    case "--compile": o.CompileOnly = true; break;
                    case "--save-only": o.SaveOnly = true; break;
                    case "--device": o.Device = next(); break;
                    case "--screens": o.Screens = next(); break;
                    case "--exclude": o.Exclude = next(); break;
                    case "--pid": o.Pid = int.Parse(next()); break;
                    case "--log": o.LogFile = next(); break;
                    case "-h": case "--help": case "/?": return null;
                    default: throw new ArgumentException("Option inconnue : " + args[i]);
                }
            }
            if (o.Mode != "stretch" && o.Mode != "uniform") throw new ArgumentException("--mode doit valoir stretch ou uniform");
            if (!o.ListOnly && o.Dump == null && !o.CompileOnly && !o.SaveOnly && o.ToW == 0) throw new ArgumentException("--to est obligatoire (ex. --to MTP1200 ou --to 1280x800)");
            return o;
        }

        public static void PrintHelp()
        {
            Console.WriteLine(@"
HmiResize - adapte les vues d'un IHM WinCC Unified à une nouvelle résolution (TIA Portal Openness)

Usage :
  HmiResize.exe --to <résolution> [options]
  HmiResize.exe --list

Résolution : LARGEURxHAUTEUR (ex. 1280x800) ou nom de pupitre :
  MTP400=480x272  MTP700=800x480  MTP1000/MTP1200=1280x800  MTP1500/1900/2200=1920x1080

Options :
  --from <rés.|auto>  Résolution d'origine des vues (défaut : auto = taille de vue la plus fréquente)
  --to <rés.>         Résolution cible (obligatoire)
  --mode stretch      Étire X et Y indépendamment pour remplir tout l'écran (défaut)
  --mode uniform      Même facteur en X et Y (min des deux), garde les proportions
  --font auto         Polices x min(facteurX, facteurY) (défaut)
  --font none         Ne touche pas aux polices
  --font <facteur>    Facteur de police imposé, ex. 1.5
  --lines             Met aussi à l'échelle les épaisseurs de traits et bordures
  --no-parts          Ne touche pas aux sous-éléments des contrôles (colonnes, polices internes...)
  --no-scripts        Ne modifie pas les coordonnées écrites en chiffres dans les OpenScreenInPopup
  --device <nom>      IHM à traiter si le projet en contient plusieurs (joker * ? accepté)
  --screens <a;b*>    Ne traite que ces vues (séparées par ;, joker * ? accepté)
  --exclude <a;b*>    Vues à ne pas traiter
  --dry-run           Simulation : affiche ce qui serait fait, ne modifie rien
  --save              Enregistre le projet à la fin
  --pid <n>           Processus TIA Portal à utiliser s'il y en a plusieurs ouverts
  --log <fichier>     Écrit aussi le rapport dans un fichier
  --list              Liste les IHM et leurs vues (taille) puis quitte
  --compile           Compile le logiciel IHM et affiche les erreurs, puis quitte
  --save-only         Enregistre le projet ouvert, puis quitte
  --dump <a;b*>       Affiche tous les objets et propriétés de ces vues (diagnostic), puis quitte

Toutes les modifications sont faites dans une seule transaction : un seul Ctrl+Z
(Annuler) dans TIA Portal suffit pour revenir en arrière.
ATTENTION : relancer le programme sur des vues déjà adaptées les agrandit à nouveau.
");
        }
    }

    // ------------------------------------------------------------------------------------------
    // Point d'entrée : prépare la résolution des DLL Openness avant de toucher aux types Siemens
    // ------------------------------------------------------------------------------------------
    static class Program
    {
        static readonly List<string> SearchDirs = new List<string>();

        static int Main(string[] args)
        {
            Options opt;
            try
            {
                opt = Options.Parse(args);
                if (opt == null) { Options.PrintHelp(); return 0; }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Erreur : " + ex.Message);
                Options.PrintHelp();
                return 2;
            }

            InitAssemblyResolver();
            try
            {
                return Run(opt);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("ERREUR : " + ex.Message);
                if (ex.GetType().Name.Contains("Security"))
                    Console.Error.WriteLine("-> Vérifiez que l'utilisateur Windows est membre du groupe \"Siemens TIA Openness\" (puis fermer/rouvrir la session).");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        // Cherche les DLL Openness de la version la plus récente installée (registre Siemens),
        // sans les copier à côté de l'exe (recommandation Siemens).
        static void InitAssemblyResolver()
        {
            string version = typeof(Program).Assembly.GetReferencedAssemblies()
                .Where(a => a.Name == "Siemens.Engineering.Base").Select(a => a.Version.Major).FirstOrDefault().ToString();
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Siemens\Automation\Openness\" + version + @".0\PublicAPI\" + version + @".0.0.0\net48"))
            {
                if (k != null)
                {
                    object p = k.GetValue("Siemens.Engineering.Base");
                    if (p != null) SearchDirs.Add(Path.GetDirectoryName(p.ToString()));
                }
            }
            string portal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Siemens\Automation\Portal V" + version);
            SearchDirs.Add(Path.Combine(portal, @"PublicAPI\V" + version + @"\net48"));
            SearchDirs.Add(Path.Combine(portal, @"Bin\PublicAPI"));

            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string name = new AssemblyName(e.Name).Name;
                foreach (string d in SearchDirs)
                {
                    string f = Path.Combine(d, name + ".dll");
                    if (File.Exists(f)) return Assembly.LoadFrom(f);
                }
                return null;
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run(Options opt)
        {
            return new Resizer(opt).Run();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Le travail
    // ------------------------------------------------------------------------------------------
    class Resizer
    {
        readonly Options opt;
        double sx, sy, sk, sf;   // facteurs X, Y, "isotrope" (rayons, traits), police
        TextWriter log;
        readonly List<string> warnings = new List<string>();
        int itemsDone, itemsSkipped, propsChanged, scriptsChanged;

        // Propriétés dont une dynamisation (script/tag) peut annuler l'adaptation
        static readonly HashSet<string> GeometryProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Left", "Top", "Width", "Height", "X1", "X2", "Y1", "Y2", "CenterX", "CenterY", "Radius", "RadiusX", "RadiusY", "Points" };

        // Compositions à ne jamais parcourir
        static readonly HashSet<string> SkipCompositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "EventHandlers", "Dynamizations", "PropertyEventHandlers", "Points", "ScreenItems" };

        public Resizer(Options o) { opt = o; }

        void Log(string fmt, params object[] args)
        {
            string s = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, fmt, args) : fmt;
            Console.WriteLine(s);
            if (log != null) log.WriteLine(s);
        }

        void Warn(string fmt, params object[] args)
        {
            string s = string.Format(CultureInfo.InvariantCulture, fmt, args);
            warnings.Add(s);
            Log("   ! " + s);
        }

        public int Run()
        {
            if (opt.LogFile != null) log = new StreamWriter(opt.LogFile, false, System.Text.Encoding.UTF8);
            try
            {
                return RunCore();
            }
            finally
            {
                if (log != null) log.Dispose();
            }
        }

        int RunCore()
        {
            // --- Connexion à TIA Portal ------------------------------------------------------
            IList<TiaPortalProcess> procs = TiaPortal.GetProcesses();
            if (procs.Count == 0) throw new Exception("Aucun TIA Portal ouvert.");
            TiaPortalProcess proc;
            if (opt.Pid != 0)
            {
                proc = procs.FirstOrDefault(p => p.Id == opt.Pid);
                if (proc == null) throw new Exception("Aucun TIA Portal avec le PID " + opt.Pid);
            }
            else
            {
                List<TiaPortalProcess> withProject = procs.Where(p => p.ProjectPath != null).ToList();
                if (withProject.Count == 1) proc = withProject[0];
                else if (procs.Count == 1) proc = procs[0];
                else
                {
                    Log("Plusieurs TIA Portal sont ouverts, précisez --pid :");
                    foreach (TiaPortalProcess p in procs) Log("  PID {0}  {1}", p.Id, p.ProjectPath != null ? p.ProjectPath.FullName : "(pas de projet)");
                    return 2;
                }
            }
            Log("Connexion à TIA Portal (PID {0})... Acceptez la demande d'accès Openness dans TIA si elle s'affiche.", proc.Id);

            using (TiaPortal portal = proc.Attach())
            {
                Project project = portal.Projects.FirstOrDefault();
                if (project == null) throw new Exception("Aucun projet ouvert dans TIA Portal.");
                Log("Projet : {0}", project.Name);

                if (opt.SaveOnly)
                {
                    project.Save();
                    Log("Projet enregistré.");
                    return 0;
                }

                List<KeyValuePair<Device, HmiSoftware>> hmis = FindUnifiedHmis(project).ToList();
                if (hmis.Count == 0) throw new Exception("Aucun IHM WinCC Unified trouvé dans le projet.");

                if (opt.ListOnly)
                {
                    foreach (var h in hmis)
                    {
                        Log("");
                        Log("IHM \"{0}\"  (appareil \"{1}\", {2})", h.Value.Name, h.Key.Name, h.Key.TypeIdentifier);
                        foreach (var s in AllScreens(h.Value))
                            Log("   {0,-40} {1}x{2}  ({3} objets)  [{4}]", s.Value.Name, s.Value.Width, s.Value.Height, s.Value.ScreenItems.Count, s.Key);
                    }
                    return 0;
                }

                if (opt.CompileOnly)
                {
                    foreach (var h in hmis.Where(h => opt.Device == null || Wildcard.IsMatch(h.Value.Name, opt.Device)))
                        Compile(h.Value);
                    return 0;
                }

                if (opt.Dump != null)
                {
                    foreach (var h in hmis)
                        foreach (var s in AllScreens(h.Value).Where(s => Wildcard.IsMatchAny(s.Value.Name, opt.Dump)))
                            DumpScreen(s.Value);
                    return 0;
                }

                if (opt.Device != null)
                    hmis = hmis.Where(h => Wildcard.IsMatch(h.Value.Name, opt.Device) || Wildcard.IsMatch(h.Key.Name, opt.Device)).ToList();
                if (hmis.Count == 0) throw new Exception("Aucun IHM ne correspond à --device " + opt.Device);
                if (hmis.Count > 1)
                {
                    Log("Le projet contient plusieurs IHM Unified, précisez --device :");
                    foreach (var h in hmis) Log("  {0}  (appareil {1})", h.Value.Name, h.Key.Name);
                    return 2;
                }

                HmiSoftware hmi = hmis[0].Value;
                List<HmiScreen> screens = AllScreens(hmi).Select(s => s.Value)
                    .Where(s => opt.Screens == null || Wildcard.IsMatchAny(s.Name, opt.Screens))
                    .Where(s => opt.Exclude == null || !Wildcard.IsMatchAny(s.Name, opt.Exclude))
                    .ToList();
                if (screens.Count == 0) throw new Exception("Aucune vue à traiter.");

                // --- Résolution d'origine ------------------------------------------------------
                if (opt.FromW == 0)
                {
                    var top = AllScreens(hmi).Select(s => s.Value).GroupBy(s => new { W = (int)s.Width, H = (int)s.Height })
                        .OrderByDescending(g => g.Count()).First().Key;
                    opt.FromW = top.W; opt.FromH = top.H;
                    Log("Résolution d'origine détectée : {0}x{1} (taille de vue la plus fréquente)", opt.FromW, opt.FromH);
                    if (opt.FromW == opt.ToW && opt.FromH == opt.ToH)
                        throw new Exception("Les vues font déjà " + opt.ToW + "x" + opt.ToH + ". Si leur contenu est encore à l'ancienne taille, indiquez --from (ex. --from MTP700).");
                }

                sx = (double)opt.ToW / opt.FromW;
                sy = (double)opt.ToH / opt.FromH;
                if (opt.Mode == "uniform") sx = sy = Math.Min(sx, sy);
                sk = Math.Min(sx, sy);
                if (opt.Font == "auto") sf = sk;
                else if (opt.Font == "none") sf = 1;
                else if (!double.TryParse(opt.Font.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out sf) || sf <= 0)
                    throw new Exception("--font invalide : " + opt.Font);

                Log("IHM : {0}   {1}x{2} -> {3}x{4}   facteurs X={5:0.####} Y={6:0.####} police={7:0.####}  mode={8}{9}",
                    hmi.Name, opt.FromW, opt.FromH, opt.ToW, opt.ToH, sx, sy, sf, opt.Mode, opt.DryRun ? "   [SIMULATION]" : "");
                Log("{0} vue(s) à traiter.", screens.Count);
                CollectScreenWindows(hmi);
                foreach (HmiScreen s in screens) processedScreens.Add(s.Name);

                // --- Traitement, dans une transaction (un seul "Annuler" dans TIA) ---------------
                using (ExclusiveAccess access = portal.ExclusiveAccess("HmiResize : adaptation des vues à " + opt.ToW + "x" + opt.ToH + "..."))
                {
                    Transaction tr = opt.DryRun ? null : access.Transaction(project, "HmiResize " + opt.FromW + "x" + opt.FromH + " -> " + opt.ToW + "x" + opt.ToH);
                    try
                    {
                        int n = 0;
                        foreach (HmiScreen screen in screens)
                        {
                            if (access.IsCancellationRequested) throw new OperationCanceledException("Annulé depuis TIA Portal : aucune modification conservée.");
                            n++;
                            access.Text = string.Format("HmiResize : vue {0}/{1} - {2}", n, screens.Count, screen.Name);
                            ProcessScreen(screen);
                        }
                        if (tr != null) tr.CommitOnDispose();
                    }
                    finally
                    {
                        if (tr != null) tr.Dispose();
                    }
                }

                Log("");
                Log("Terminé : {0} vue(s), {1} objet(s) adapté(s), {2} objet(s) ignoré(s), {3} propriété(s) modifiée(s), {4} appel(s) OpenScreenInPopup recalé(s){5}.",
                    screens.Count, itemsDone, itemsSkipped, propsChanged, scriptsChanged, opt.DryRun ? " (simulation, rien n'a été modifié)" : "");
                if (warnings.Count > 0)
                {
                    Log("{0} avertissement(s) à vérifier à la main :", warnings.Count);
                    foreach (string w in warnings.Distinct()) Log("  - " + w);
                }

                if (opt.Save && !opt.DryRun)
                {
                    project.Save();
                    Log("Projet enregistré.");
                }
                else if (!opt.DryRun)
                {
                    Log("Projet NON enregistré (Ctrl+Z dans TIA pour annuler, ou --save pour enregistrer automatiquement).");
                }
            }
            return 0;
        }

        // ----------------------------------------------------------------------------------
        // Parcours du projet
        // ----------------------------------------------------------------------------------
        static IEnumerable<KeyValuePair<Device, HmiSoftware>> FindUnifiedHmis(Project project)
        {
            foreach (Device d in AllDevices(project.Devices, project.DeviceGroups))
                foreach (DeviceItem di in AllDeviceItems(d.DeviceItems))
                {
                    SoftwareContainer sc = di.GetService<SoftwareContainer>();
                    if (sc != null && sc.Software is HmiSoftware)
                        yield return new KeyValuePair<Device, HmiSoftware>(d, (HmiSoftware)sc.Software);
                }
        }

        static IEnumerable<Device> AllDevices(DeviceComposition devices, DeviceUserGroupComposition groups)
        {
            foreach (Device d in devices) yield return d;
            foreach (DeviceUserGroup g in groups)
                foreach (Device d in AllDevices(g.Devices, g.Groups)) yield return d;
        }

        static IEnumerable<DeviceItem> AllDeviceItems(DeviceItemComposition items)
        {
            foreach (DeviceItem di in items)
            {
                yield return di;
                foreach (DeviceItem sub in AllDeviceItems(di.DeviceItems)) yield return sub;
            }
        }

        // Clé = chemin du dossier de la vue
        static IEnumerable<KeyValuePair<string, HmiScreen>> AllScreens(HmiSoftware hmi)
        {
            foreach (HmiScreen s in hmi.Screens) yield return new KeyValuePair<string, HmiScreen>("", s);
            foreach (HmiScreenGroup g in hmi.ScreenGroups)
                foreach (var s in ScreensInGroup(g, g.Name)) yield return s;
        }

        static IEnumerable<KeyValuePair<string, HmiScreen>> ScreensInGroup(HmiScreenGroup g, string path)
        {
            foreach (HmiScreen s in g.Screens) yield return new KeyValuePair<string, HmiScreen>(path, s);
            foreach (HmiScreenGroup sub in g.Groups)
                foreach (var s in ScreensInGroup(sub, path + "/" + sub.Name)) yield return s;
        }

        // Taille future des fenêtres de vue, par nom de vue affichée : une vue partielle
        // (bandeau, zone de contenu) prend exactement la taille de la fenêtre qui l'affiche.
        readonly Dictionary<string, int[]> windowSizes = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        void CollectScreenWindows(HmiSoftware hmi)
        {
            foreach (var s in AllScreens(hmi))
                foreach (IEngineeringObject item in s.Value.ScreenItems.Cast<IEngineeringObject>())
                {
                    if (item.GetType().Name != "HmiScreenWindow") continue;
                    string target = SafeAttr(item, "Screen");
                    if (string.IsNullOrEmpty(target) || target == "?") continue;
                    var c = new List<KeyValuePair<string, object>>();
                    ScaleBox(item, c);
                    int w = Convert.ToInt32(item.GetAttribute("Width")), h = Convert.ToInt32(item.GetAttribute("Height"));
                    int nw = w, nh = h;
                    foreach (var kv in c)
                    {
                        if (kv.Key == "Width") nw = Convert.ToInt32(kv.Value);
                        if (kv.Key == "Height") nh = Convert.ToInt32(kv.Value);
                    }
                    if (!windowSizes.ContainsKey(target)) windowSizes[target] = new[] { w, h, nw, nh };
                    // Les vues chargées dynamiquement (ChangeScreen) dans une fenêtre ont la même taille
                    // qu'elle : on retient aussi la correspondance par taille.
                    string key = w + "x" + h;
                    int[] prev;
                    if (!windowSizesBySize.TryGetValue(key, out prev)) windowSizesBySize[key] = new[] { w, h, nw, nh };
                    else if (prev != null && (prev[2] != nw || prev[3] != nh)) windowSizesBySize[key] = null;   // ambigu
                }
        }

        readonly Dictionary<string, int[]> windowSizesBySize = new Dictionary<string, int[]>();
        readonly HashSet<string> processedScreens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ----------------------------------------------------------------------------------
        // Une vue
        // ----------------------------------------------------------------------------------
        void ProcessScreen(HmiScreen screen)
        {
            int w = (int)screen.Width, h = (int)screen.Height;
            int nw, nh;
            string kind = "";
            int[] win;
            bool fullScreen = (w == opt.FromW && h == opt.FromH) || (w == opt.ToW && h == opt.ToH);
            if (fullScreen) { nw = opt.ToW; nh = opt.ToH; }
            else if (windowSizes.TryGetValue(screen.Name, out win) && win[0] == w && win[1] == h)
            {
                nw = win[2]; nh = win[3]; kind = " (taille de sa fenêtre de vue)";
            }
            else if (windowSizesBySize.TryGetValue(w + "x" + h, out win) && win != null)
            {
                nw = win[2]; nh = win[3]; kind = " (taille d'une fenêtre de vue de même taille)";
            }
            else { nw = Round(w * sx); nh = Round(h * sy); kind = " (vue partielle / pop-up)"; }

            Log("");
            Log("Vue \"{0}\" : {1}x{2} -> {3}x{4}{5}, {6} objet(s)", screen.Name, w, h, nw, nh, kind, screen.ScreenItems.Count);

            if (!opt.DryRun && (nw != w || nh != h))
            {
                screen.Width = (uint)nw;
                screen.Height = (uint)nh;
                propsChanged += 2;
            }

            foreach (IEngineeringObject item in screen.ScreenItems.Cast<IEngineeringObject>().ToList())
            {
                try
                {
                    if (ProcessItem(item, screen.Name)) itemsDone++; else itemsSkipped++;
                }
                catch (Exception ex)
                {
                    itemsSkipped++;
                    Warn("{0}/{1} : erreur {2}", screen.Name, ItemName(item), ex.Message);
                }
            }
        }

        // ----------------------------------------------------------------------------------
        // Un objet de vue
        // ----------------------------------------------------------------------------------
        bool ProcessItem(IEngineeringObject item, string screenName)
        {
            HashSet<string> a = Attrs(item);
            string name = screenName + "/" + ItemName(item);
            var changes = new List<KeyValuePair<string, object>>();
            bool geometry = true;

            if (a.Contains("X1") && a.Contains("Y2"))
            {
                // Ligne : on déplace les deux extrémités
                Scale(item, changes, "X1", sx); Scale(item, changes, "Y1", sy);
                Scale(item, changes, "X2", sx); Scale(item, changes, "Y2", sy);
            }
            else if (a.Contains("CenterX") && a.Contains("CenterY"))
            {
                // Cercle, ellipse, arcs, segments
                Scale(item, changes, "CenterX", sx); Scale(item, changes, "CenterY", sy);
                if (a.Contains("Radius")) Scale(item, changes, "Radius", sk);
                if (a.Contains("RadiusX")) Scale(item, changes, "RadiusX", sx);
                if (a.Contains("RadiusY")) Scale(item, changes, "RadiusY", sy);
            }
            else if (HasComposition(item, "Points"))
            {
                // Polyligne / polygone
                ScalePointBased(item, a, name, changes);
            }
            else if (a.Contains("Left") && a.Contains("Top") && a.Contains("Width") && a.Contains("Height"))
            {
                ScaleBox(item, changes);
            }
            else if (a.Contains("Left") && a.Contains("Top"))
            {
                Scale(item, changes, "Left", sx); Scale(item, changes, "Top", sy);
            }
            else
            {
                geometry = false;
                Warn("{0} ({1}) : pas de position connue, non déplacé", name, item.GetType().Name);
            }

            if (opt.ScaleLines)
            {
                if (a.Contains("LineWidth")) ScaleMin1(item, changes, "LineWidth", sk);
                if (a.Contains("BorderWidth")) ScaleMin1(item, changes, "BorderWidth", sk);
            }

            Apply(item, changes);

            // Police et marges internes de l'objet (sous-objets en lecture seule, modifiés en place)
            HashSet<string> readable = Attrs(item, false);
            if (readable.Contains("Font")) ScaleFont(item.GetAttribute("Font") as IEngineeringObject);
            if (readable.Contains("Padding")) ScalePadding(item.GetAttribute("Padding") as IEngineeringObject);

            string type = item.GetType().Name;

            // Sous-éléments des contrôles (colonnes de tableaux, barres d'outils, polices internes...)
            if (opt.ScaleParts && !NoPartsTypes.Contains(type)) ScaleParts(item, 0);
            else if (opt.ScaleParts) Warn("{0} ({1}) : seul le cadre est adapté (polices et colonnes internes à vérifier)", name, type);

            CheckDynamizations(item, name);
            CheckScripts(item, name);

            if (type == "HmiFaceplateContainer")
                Warn("{0} : conteneur de faceplate redimensionné ; le contenu du type de faceplate n'est pas modifié (vérifier la propriété \"Adaption\")", name);
            else if (type == "HmiScreenWindow" && !processedScreens.Contains(SafeAttr(item, "Screen")))
                Warn("{0} : fenêtre de vue redimensionnée, mais la vue \"{1}\" qu'elle affiche n'est pas traitée", name, SafeAttr(item, "Screen"));
            else if (type == "HmiGraphicView")
                Warn("{0} : vue graphique étirée ; vérifier le mode d'étirement de l'image", name);

            return geometry;
        }

        void ScaleBox(IEngineeringObject item, List<KeyValuePair<string, object>> changes)
        {
            // On met à l'échelle les bords (gauche/droite, haut/bas) plutôt que la largeur seule :
            // deux objets jointifs restent jointifs malgré les arrondis.
            object oL = item.GetAttribute("Left"), oT = item.GetAttribute("Top");
            object oW = item.GetAttribute("Width"), oH = item.GetAttribute("Height");
            double L = ToD(oL), T = ToD(oT), W = ToD(oW), H = ToD(oH);
            int nL = Round(L * sx), nT = Round(T * sy);
            int nW = Round((L + W) * sx) - nL, nH = Round((T + H) * sy) - nT;
            if (W > 0 && nW < 1) nW = 1;
            if (H > 0 && nH < 1) nH = 1;
            Add(changes, "Left", oL, nL); Add(changes, "Top", oT, nT);
            Add(changes, "Width", oW, nW); Add(changes, "Height", oH, nH);
        }

        void ScalePointBased(IEngineeringObject item, HashSet<string> a, string name, List<KeyValuePair<string, object>> changes)
        {
            // Dans Unified, les points d'une polyligne/d'un polygone sont relatifs à Left/Top :
            // on met à l'échelle l'origine et chaque point.
            if (a.Contains("Left")) Scale(item, changes, "Left", sx);
            if (a.Contains("Top")) Scale(item, changes, "Top", sy);
            Apply(item, changes);
            changes.Clear();

            IEnumerable points = item.GetComposition("Points") as IEnumerable;
            if (points == null) return;
            foreach (IEngineeringObject p in points.Cast<IEngineeringObject>().ToList())
            {
                var pc = new List<KeyValuePair<string, object>>();
                Scale(p, pc, "X", sx);
                Scale(p, pc, "Y", sy);
                Apply(p, pc);
            }
        }

        void ScaleFont(IEngineeringObject font)
        {
            if (font == null || sf == 1) return;
            if (!Attrs(font).Contains("Size")) return;
            object o = font.GetAttribute("Size");
            double size = ToD(o);
            if (size <= 0) return;
            double ns = Math.Max(1, Math.Round(size * sf));
            var c = new List<KeyValuePair<string, object>>();
            Add(c, "Size", o, ns);
            Apply(font, c);
        }

        void ScalePadding(IEngineeringObject pad)
        {
            if (pad == null) return;
            HashSet<string> a = Attrs(pad);
            var c = new List<KeyValuePair<string, object>>();
            if (a.Contains("Left")) Scale(pad, c, "Left", sx);
            if (a.Contains("Right")) Scale(pad, c, "Right", sx);
            if (a.Contains("Top")) Scale(pad, c, "Top", sy);
            if (a.Contains("Bottom")) Scale(pad, c, "Bottom", sy);
            Apply(pad, c);
        }

        // Objets dont on ne parcourt pas les sous-éléments : la lecture de certains d'entre eux
        // fait planter TIA Portal V21 (ex. HmiSystemDiagnosisControl.ScriptDiagnosisView).
        static readonly HashSet<string> NoPartsTypes = new HashSet<string> { "HmiSystemDiagnosisControl" };

        // Sous-éléments jamais lus (même raison)
        static readonly HashSet<string> NoReadProps = new HashSet<string> { "ScriptDiagnosisView", "ScriptDiagnosisOverviewText" };

        // Sous-éléments ("parts") et listes de sous-éléments d'un type, déterminés par réflexion
        // sur l'API (sans interroger TIA, pour ne lire que ce qui est utile).
        static readonly Dictionary<Type, PropertyInfo[]> partProps = new Dictionary<Type, PropertyInfo[]>();
        static readonly Dictionary<Type, PropertyInfo[]> partListProps = new Dictionary<Type, PropertyInfo[]>();

        static bool IsPartType(Type t) { return t != null && t.Name.EndsWith("Part") && typeof(IEngineeringObject).IsAssignableFrom(t); }

        static Type ElementType(Type t)
        {
            foreach (Type i in t.GetInterfaces())
                if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
            return null;
        }

        static void ReflectParts(Type t, out PropertyInfo[] parts, out PropertyInfo[] lists)
        {
            if (!partProps.TryGetValue(t, out parts))
            {
                PropertyInfo[] all = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.Name != "Parent" && !NoReadProps.Contains(p.Name)
                                && !SkipCompositions.Contains(p.Name)).ToArray();
                parts = all.Where(p => IsPartType(p.PropertyType) && p.Name != "Font" && p.Name != "Padding").ToArray();
                lists = all.Where(p => p.PropertyType != typeof(string) && IsPartType(ElementType(p.PropertyType))).ToArray();
                partProps[t] = parts;
                partListProps[t] = lists;
            }
            lists = partListProps[t];
        }

        // Parcourt les "parts" (sous-objets) et leurs listes : colonnes (Width), hauteur de
        // ligne des tableaux, éléments de barre d'outils (Width/Height), polices internes.
        void ScaleParts(IEngineeringObject obj, int depth)
        {
            if (depth > 6) return;
            PropertyInfo[] parts, lists;
            ReflectParts(obj.GetType(), out parts, out lists);
            foreach (PropertyInfo p in parts)
            {
                IEngineeringObject part;
                try { part = p.GetValue(obj, null) as IEngineeringObject; } catch { continue; }
                if (part != null) ScalePart(part, depth + 1);
            }
            foreach (PropertyInfo p in lists)
            {
                IEnumerable list;
                try { list = p.GetValue(obj, null) as IEnumerable; } catch { continue; }
                if (list == null) continue;
                foreach (IEngineeringObject part in list.Cast<object>().OfType<IEngineeringObject>().ToList())
                    ScalePart(part, depth + 1);
            }
        }

        void ScalePart(IEngineeringObject part, int depth)
        {
            HashSet<string> a = Attrs(part);
            string t = part.GetType().Name;
            if (t == "HmiFontPart") { ScaleFont(part); return; }
            if (t == "HmiPaddingPart") { ScalePadding(part); return; }
            var c = new List<KeyValuePair<string, object>>();
            if (a.Contains("Width")) Scale(part, c, "Width", sx);
            if (a.Contains("MinimumWidth")) Scale(part, c, "MinimumWidth", sx);
            if (a.Contains("MaximumWidth")) Scale(part, c, "MaximumWidth", sx);
            if (a.Contains("Height")) Scale(part, c, "Height", sy);
            if (a.Contains("RowHeight")) Scale(part, c, "RowHeight", sy);
            Apply(part, c);
            HashSet<string> all = Attrs(part, false);
            if (all.Contains("Font")) ScaleFont(part.GetAttribute("Font") as IEngineeringObject);
            if (all.Contains("Padding")) ScalePadding(part.GetAttribute("Padding") as IEngineeringObject);
            ScaleParts(part, depth);
        }

        // Signale les scripts qui ouvrent des pop-ups ou manipulent des positions/tailles
        static readonly System.Text.RegularExpressions.Regex GeometryScript = new System.Text.RegularExpressions.Regex(
            @"OpenScreenInPopup|\.(Left|Top|Width|Height)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        void CheckScripts(IEngineeringObject item, string name)
        {
            // Objets portant un ScriptCode : dynamisations par script et scripts d'événements
            var codes = new List<IEngineeringObject>();
            try
            {
                foreach (IEngineeringObject d in ((IEnumerable)item.GetComposition("Dynamizations")).Cast<IEngineeringObject>())
                    if (Attrs(d, false).Contains("ScriptCode")) codes.Add(d);
            }
            catch { }
            try
            {
                foreach (IEngineeringObject h in ((IEnumerable)item.GetComposition("EventHandlers")).Cast<IEngineeringObject>())
                {
                    IEngineeringObject script = Attrs(h, false).Contains("Script") ? h.GetAttribute("Script") as IEngineeringObject : null;
                    if (script != null && Attrs(script, false).Contains("ScriptCode")) codes.Add(script);
                }
            }
            catch { }
            foreach (var sc in codes)
            {
                string code = SafeAttr(sc, "ScriptCode");
                if (code == "?") continue;
                if (opt.ScaleScripts)
                {
                    int n, nl;
                    string newCode = RewritePopupCalls(code, out n, out nl);
                    if (n > 0)
                    {
                        scriptsChanged += n;
                        if (!opt.DryRun) sc.SetAttribute("ScriptCode", newCode);
                        propsChanged++;
                        code = newCode;
                    }
                }
                foreach (string line in code.Split('\n'))
                {
                    if (!GeometryScript.IsMatch(line) || line.TrimStart().StartsWith("//")) continue;
                    if (opt.ScaleScripts && line.Contains(ScaledMarker)) continue;
                    int c1, nonLiteral;
                    if (opt.ScaleScripts && line.Contains("OpenScreenInPopup") && !line.Contains(".Left") && !line.Contains(".Top")
                        && !line.Contains(".Width") && !line.Contains(".Height"))
                    {
                        RewritePopupCalls(line, out c1, out nonLiteral);
                        if (nonLiteral == 0) continue;   // coordonnées en chiffres déjà correctes (ex. 0, 0)
                    }
                    Warn("{0} : script à vérifier : {1}", name, line.Trim());
                }
            }
        }

        const string ScaledMarker = "/*HmiResize*/";

        // Met à l'échelle les arguments Left/Top (5e et 6e) des appels OpenScreenInPopup
        // lorsqu'ils sont écrits en chiffres. Les variables et expressions sont laissées telles quelles.
        string RewritePopupCalls(string code, out int count, out int nonLiteral)
        {
            count = 0;
            nonLiteral = 0;
            var sb = new System.Text.StringBuilder();
            int pos = 0;
            const string fn = "OpenScreenInPopup";
            while (true)
            {
                int i = code.IndexOf(fn, pos, StringComparison.Ordinal);
                if (i < 0) break;
                int open = i + fn.Length;
                while (open < code.Length && char.IsWhiteSpace(code[open])) open++;
                if (open >= code.Length || code[open] != '(') { sb.Append(code, pos, open - pos); pos = open; continue; }

                // Découpe des arguments (gestion des chaînes et parenthèses imbriquées)
                var argStart = new List<int>(); var argEnd = new List<int>();
                int depthP = 0, k = open + 1, start = k; char quote = '\0';
                for (; k < code.Length; k++)
                {
                    char ch = code[k];
                    if (quote != '\0') { if (ch == '\\') k++; else if (ch == quote) quote = '\0'; continue; }
                    if (ch == '"' || ch == '\'' || ch == '`') quote = ch;
                    else if (ch == '(' || ch == '[' || ch == '{') depthP++;
                    else if ((ch == ')' || ch == ']' || ch == '}') && depthP > 0) depthP--;
                    else if (ch == ',' && depthP == 0) { argStart.Add(start); argEnd.Add(k); start = k + 1; }
                    else if (ch == ')' && depthP == 0) { argStart.Add(start); argEnd.Add(k); break; }
                }
                if (k >= code.Length || argStart.Count < 6) { sb.Append(code, pos, open - pos); pos = open; continue; }

                sb.Append(code, pos, argStart[4] - pos);
                bool changed = false;
                for (int a = 4; a <= 5; a++)
                {
                    string raw = code.Substring(argStart[a], argEnd[a] - argStart[a]);
                    int v;
                    if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    {
                        int nv = Round(v * (a == 4 ? sx : sy));
                        string lead = raw.Substring(0, raw.Length - raw.TrimStart().Length);
                        sb.Append(lead).Append(nv.ToString(CultureInfo.InvariantCulture));
                        changed |= nv != v;
                    }
                    else { sb.Append(raw); nonLiteral++; }
                    sb.Append(a == 4 ? "," : "");
                }
                pos = argEnd[5];
                if (changed)
                {
                    count++;
                    // Marque la ligne pour ne pas la signaler (et pour qu'on la retrouve)
                    int eol = code.IndexOf('\n', pos);
                    if (eol < 0) eol = code.Length;
                    int lineEnd = eol > 0 && code[eol - 1] == '\r' ? eol - 1 : eol;
                    sb.Append(code, pos, lineEnd - pos).Append(" ").Append(ScaledMarker);
                    pos = lineEnd;
                }
            }
            sb.Append(code, pos, code.Length - pos);
            return sb.ToString();
        }

        void CheckDynamizations(IEngineeringObject item, string name)
        {
            IEnumerable dyns;
            try { dyns = item.GetComposition("Dynamizations") as IEnumerable; } catch { return; }
            if (dyns == null) return;
            foreach (IEngineeringObject d in dyns.Cast<IEngineeringObject>())
            {
                string prop = null;
                try { prop = Convert.ToString(d.GetAttribute("PropertyName")); } catch { }
                if (prop != null && GeometryProps.Contains(prop))
                    Warn("{0} : la propriété {1} est dynamisée ({2}) ; les valeurs calculées par le tag/script ne sont pas adaptées",
                        name, prop, SafeAttr(d, "DynamizationType"));
            }
        }

        // Compile le logiciel IHM et affiche erreurs / avertissements
        void Compile(HmiSoftware hmi)
        {
            Log("Compilation de \"{0}\"...", hmi.Name);
            // Le service de compilation est porté par l'élément d'appareil qui contient le logiciel IHM
            Siemens.Engineering.Compiler.ICompilable c = null;
            SoftwareContainer sc = hmi.Parent as SoftwareContainer;
            IEngineeringObject o = sc != null ? sc.OwnedBy : null;
            while (c == null && o != null)
            {
                IEngineeringServiceProvider sp = o as IEngineeringServiceProvider;
                if (sp != null) c = sp.GetService<Siemens.Engineering.Compiler.ICompilable>();
                o = o.Parent;
            }
            if (c == null) { Log("  Compilation non disponible via Openness pour cet IHM."); return; }
            Siemens.Engineering.Compiler.CompilerResult r = c.Compile();
            Log("  Résultat : {0}  ({1} erreur(s), {2} avertissement(s))", r.State, r.ErrorCount, r.WarningCount);
            LogCompilerMessages(r.Messages, "    ");
        }

        void LogCompilerMessages(Siemens.Engineering.Compiler.CompilerResultMessageComposition msgs, string indent)
        {
            foreach (Siemens.Engineering.Compiler.CompilerResultMessage m in msgs)
            {
                if (m.State != Siemens.Engineering.Compiler.CompilerResultState.Success || m.Messages.Count > 0)
                {
                    if (m.State != Siemens.Engineering.Compiler.CompilerResultState.Success && !string.IsNullOrEmpty(m.Description))
                        Log("{0}[{1}] {2} {3}", indent, m.State, m.Path, m.Description);
                    LogCompilerMessages(m.Messages, indent + "  ");
                }
            }
        }

        // ----------------------------------------------------------------------------------
        // Diagnostic : affiche les objets d'une vue et leurs attributs
        // ----------------------------------------------------------------------------------
        void DumpScreen(HmiScreen screen)
        {
            Log("");
            Log("=== Vue \"{0}\" {1}x{2}", screen.Name, screen.Width, screen.Height);
            foreach (IEngineeringObject item in screen.ScreenItems.Cast<IEngineeringObject>())
                DumpObject(item, "  ", 0);
        }

        void DumpObject(IEngineeringObject o, string indent, int depth)
        {
            Log("{0}[{1}] {2}", indent, o.GetType().Name, SafeAttr(o, "Name"));
            var values = new List<string>();
            foreach (string n in Attrs(o, false).OrderBy(n => n))
            {
                if (n == "Parent" || n == "Name" || NoReadProps.Contains(n)) continue;
                if (depth > 0 && NoPartsTypes.Contains(o.GetType().Name)) continue;
                object v;
                try { v = o.GetAttribute(n); } catch { continue; }
                IEngineeringObject sub = v as IEngineeringObject;
                if (sub != null)
                {
                    if (depth < 3 && sub.GetType().Name.EndsWith("Part")) { Log("{0}  .{1}:", indent, n); DumpObject(sub, indent + "    ", depth + 1); }
                    continue;
                }
                values.Add(n + "=" + Convert.ToString(v, CultureInfo.InvariantCulture));
            }
            Log("{0}  {1}", indent, string.Join("  ", values));
            try
            {
                foreach (EngineeringCompositionInfo ci in o.GetCompositionInfos())
                {
                    IEnumerable list = o.GetComposition(ci.Name) as IEnumerable;
                    int count = list == null ? 0 : list.Cast<object>().Count();
                    if (count == 0) continue;
                    Log("{0}  #{1} ({2})", indent, ci.Name, count);
                    if (ci.Name == "EventHandlers")
                        foreach (IEngineeringObject h in list.Cast<object>().OfType<IEngineeringObject>())
                        {
                            IEngineeringObject script = Attrs(h, false).Contains("Script") ? h.GetAttribute("Script") as IEngineeringObject : null;
                            if (script == null) continue;
                            Log("{0}    {1}:", indent, SafeAttr(h, "EventType"));
                            foreach (string line in SafeAttr(script, "ScriptCode").Split('\n'))
                                if (line.Trim().Length > 0) Log("{0}      {1}", indent, line.TrimEnd());
                        }
                    if (depth < 2 && ci.Name != "EventHandlers" && ci.Name != "PropertyEventHandlers")
                        foreach (IEngineeringObject sub in list.Cast<object>().OfType<IEngineeringObject>().Take(20))
                            DumpObject(sub, indent + "    ", depth + 1);
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------
        // Outils
        // ----------------------------------------------------------------------------------
        static readonly Dictionary<Type, HashSet<string>> writableCache = new Dictionary<Type, HashSet<string>>();
        static readonly Dictionary<Type, HashSet<string>> readableCache = new Dictionary<Type, HashSet<string>>();

        // Attributs (en écriture par défaut) d'un objet Openness, mis en cache par type
        static HashSet<string> Attrs(IEngineeringObject o, bool writable = true)
        {
            Type t = o.GetType();
            Dictionary<Type, HashSet<string>> cache = writable ? writableCache : readableCache;
            HashSet<string> s;
            if (!cache.TryGetValue(t, out s))
            {
                s = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    foreach (EngineeringAttributeInfo i in o.GetAttributeInfos())
                        if (!writable || (i.AccessMode & EngineeringAttributeAccessMode.Write) != 0) s.Add(i.Name);
                }
                catch { }
                cache[t] = s;
            }
            return s;
        }

        static bool HasComposition(IEngineeringObject o, string name)
        {
            try { return o.GetCompositionInfos().Any(c => c.Name == name); } catch { return false; }
        }

        void Scale(IEngineeringObject o, List<KeyValuePair<string, object>> changes, string attr, double factor)
        {
            object v = o.GetAttribute(attr);
            if (v == null) return;
            Add(changes, attr, v, ToD(v) * factor);
        }

        void ScaleMin1(IEngineeringObject o, List<KeyValuePair<string, object>> changes, string attr, double factor)
        {
            object v = o.GetAttribute(attr);
            double d = ToD(v);
            if (d <= 0) return;
            Add(changes, attr, v, Math.Max(1, d * factor));
        }

        static void Add(List<KeyValuePair<string, object>> changes, string attr, object oldValue, double newValue)
        {
            object nv = FromD(newValue, oldValue);
            if (!nv.Equals(oldValue)) changes.Add(new KeyValuePair<string, object>(attr, nv));
        }

        void Apply(IEngineeringObject o, List<KeyValuePair<string, object>> changes)
        {
            if (changes.Count == 0) return;
            propsChanged += changes.Count;
            if (opt.DryRun) return;
            o.SetAttributes(changes);
        }

        static double ToD(object v) { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }

        // Reconvertit dans le type d'origine de l'attribut (Int32, UInt32, Byte, Single...)
        static object FromD(double d, object like)
        {
            Type t = like.GetType();
            if (t == typeof(float)) return (float)d;
            if (t == typeof(double)) return d;
            double r = Math.Round(d, MidpointRounding.AwayFromZero);
            if (t == typeof(int)) return (int)r;
            if (t == typeof(uint)) return (uint)Math.Max(0, r);
            if (t == typeof(byte)) return (byte)Math.Max(0, Math.Min(255, r));
            if (t == typeof(short)) return (short)r;
            if (t == typeof(ushort)) return (ushort)Math.Max(0, r);
            if (t == typeof(long)) return (long)r;
            return Convert.ChangeType(r, t, CultureInfo.InvariantCulture);
        }

        static int Round(double d) { return (int)Math.Round(d, MidpointRounding.AwayFromZero); }

        static string ItemName(IEngineeringObject o) { return SafeAttr(o, "Name"); }

        static string SafeAttr(IEngineeringObject o, string attr)
        {
            try { return Convert.ToString(o.GetAttribute(attr), CultureInfo.InvariantCulture); } catch { return "?"; }
        }
    }

    static class Wildcard
    {
        public static bool IsMatch(string text, string pattern)
        {
            string re = "^" + System.Text.RegularExpressions.Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(text, re, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static bool IsMatchAny(string text, string patterns)
        {
            return patterns.Split(';').Where(p => p.Trim().Length > 0).Any(p => IsMatch(text, p));
        }
    }
}
