using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scripts
{
    internal sealed class LoadedScript
    {
        public string FileName;
        public string Text;
        public string Error;
    }

    /// <summary>Plain .txt scripts in BepInEx/config/Scripts. Read from disk on demand (Reload), nothing cached across reloads.</summary>
    internal sealed class ScriptLibrary
    {
        public const string ExampleName = "Example Script.txt";

        public string Folder { get; }
        public IReadOnlyList<string> Files => _files;

        private readonly List<string> _files = new List<string>();

        public ScriptLibrary(string folder)
        {
            Folder = folder;
        }

        public void EnsureFolder()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                bool any = Directory.EnumerateFiles(Folder, "*.txt").Any();
                if (!any)
                    File.WriteAllText(Path.Combine(Folder, ExampleName), ExampleText, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not create the Scripts folder: " + e.Message);
            }
        }

        public void Refresh()
        {
            _files.Clear();
            try
            {
                EnsureFolder();
                _files.AddRange(Directory.EnumerateFiles(Folder, "*.txt")
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not list scripts: " + e.Message);
            }
        }

        /// <summary>Resolves a wanted file name to an existing one (falls back to the first file).</summary>
        public string Resolve(string wanted)
        {
            if (!string.IsNullOrEmpty(wanted))
            {
                string hit = _files.FirstOrDefault(f => string.Equals(f, wanted, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
            return _files.Count > 0 ? _files[0] : null;
        }

        public string Neighbor(string current, int step)
        {
            if (_files.Count == 0) return null;
            int i = _files.FindIndex(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return _files[0];
            return _files[((i + step) % _files.Count + _files.Count) % _files.Count];
        }

        public LoadedScript Load(string wanted)
        {
            string name = Resolve(wanted);
            if (name == null)
                return new LoadedScript { FileName = "(none)", Text = "", Error = "No .txt files in BepInEx/config/Scripts" };

            try
            {
                // ReadAllText detects UTF-8/UTF-16 BOMs; UTF-8 without BOM is the default.
                string text = File.ReadAllText(Path.Combine(Folder, name));
                text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ").TrimEnd();
                return new LoadedScript { FileName = name, Text = text, Error = text.Length == 0 ? "Script file is empty" : null };
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read script '{name}': {e.Message}");
                return new LoadedScript { FileName = name, Text = "", Error = "Could not read file (is it open elsewhere?)" };
            }
        }

        private const string ExampleText =
@"Welcome to Scripts, the in-game teleprompter.

Paste your own script into a .txt file in this folder:
BepInEx/config/Scripts/

Press B on your right controller to open the settings menu.
Press Play to start scrolling, and Pause to hold your place.
Press Play again to continue from where you stopped.

Edit the file on your PC, save it, and press Reload in the menu.
You do not need to restart the game.

The panel is only visible inside your headset.
Your recording camera, the desktop spectator view and other players cannot see it.

Use Speed to set how fast the text moves.
Use Text Size, Distance and Opacity until it feels comfortable to read.

Head mode keeps the panel in front of your eyes.
Body mode keeps it in front of your body, so you can glance around without it moving.

Blank lines are kept, so you can use them to space out paragraphs and take a breath.

When the script reaches the end it stops. Press Restart for another take.

Good luck with your video!";
    }
}
