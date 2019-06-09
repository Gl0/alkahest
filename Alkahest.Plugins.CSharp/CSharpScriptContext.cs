using Alkahest.Core.Data;
using Alkahest.Core.Logging;
using Alkahest.Core.Net.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Alkahest.Plugins.CSharp
{
    public sealed class CSharpScriptContext
    {
        static readonly IReadOnlyList<Color> _colors;

        static int _last;

        public string Name { get; }

        public string Path { get; }

        public Log Log { get; }

        public DataCenter Data { get; }

        public IReadOnlyList<GameProxy> Proxies { get; }

        readonly CSharpPlugin _plugin;

        readonly Color _scriptColor;

        static CSharpScriptContext()
        {
            var rand = new Random();

            _colors = new[]
            {
                Color.Fuchsia,
                Color.Yellow,
                Color.Blue,
                Color.Teal,
                Color.Aqua,
                Color.Orange,
                Color.Azure,
                Color.Beige,
                Color.Bisque,
                Color.Chocolate,
                Color.Coral,
                Color.Cornsilk,
                Color.Gold,
                Color.Ivory,
                Color.Khaki,
                Color.Lavender,
                Color.Orchid,
                Color.Peru,
                Color.Pink,
                Color.Plum,
                Color.Tan,
                Color.Thistle,
                Color.Tomato,
                Color.Violet,
                Color.Wheat,
            }.Select(x => Color.FromArgb(0, x)).OrderBy(x => rand.Next()).ToArray();
        }

        internal CSharpScriptContext(CSharpPlugin plugin, string name, string path, Log log,
            DataCenter data, IEnumerable<GameProxy> proxies)
        {
            Name = name;
            Path = path;
            Data = data;
            Log = log;
            Proxies = proxies.ToArray();
            _plugin = plugin;
            _scriptColor = _colors[_last++ % _colors.Count];
        }

        public JObject LoadConfiguration(int version, Func<JObject, int, JObject> migrator = null)
        {
            var path = GetConfigurationPath();

            if (!File.Exists(path))
                File.Copy(path + ".default", path);

            using var reader = new JsonTextReader(new StreamReader(path));
            var value = JObject.Load(reader);

            if (value["version"] == null)
                value["version"] = 0;

            var ver = (int)value["version"];

            return ver != version && migrator != null ? migrator(value, ver) : value;
        }

        public void SaveConfiguration(JObject value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            using var writer = new JsonTextWriter(new StreamWriter(GetConfigurationPath()))
            {
                Formatting = Formatting.Indented,
            };

            if (value["version"] == null)
                value["version"] = 0;

            value.WriteTo(writer);
        }

        string GetConfigurationPath()
        {
            return System.IO.Path.Combine(Path, "config.json");
        }

        public void Message(GameClient client, string format, params object[] args)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            _plugin.Message(client, $"<FONT COLOR=\"#{_scriptColor.ToArgb():X}\">{Name}</FONT>", format, args);
        }

        static void CheckName(string name)
        {
            if (!name.All(x => char.IsLetterOrDigit(x) || x == '-' || x == '_'))
                throw new ArgumentException("Invalid command name.", nameof(name));
        }

        public bool AddCommand(string name, Action<GameClient, string[]> handler)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            CheckName(name);

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return _plugin.AddCommand(this, name, handler);
        }

        public bool RemoveCommand(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            CheckName(name);

            return _plugin.RemoveCommand(this, name);
        }
    }
}
