﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using YamlDotNet;
using YamlDotNet.Serialization;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using ConfigFramework;

namespace PluginLoader
{
    public interface IPlugin
    {
        string GetPluginName();
        Version GetPluginVersion();
        string GetPluginDescription();
        string GetPluginAuthor();
        Guid GetPluginID();
        string[] GetPluginDependencies();

        void OnPluginLoaded(PluginLoader loader);
        void OnPluginUnloaded(PluginLoader loader);
        void OnPluginEnabled();
        void OnPluginDisabled();
        void OnPluginToggled();
    }

    public abstract class Plugin : IPlugin
    {
        private string name = "";
        private Version version = null;
        private string author;
        private string description = "";
        private Guid id = Guid.Empty;
        private string[] dependencies = new string[0];
        private bool toggled = false;
        private FileInfo pluginFile = null;
        private PluginLoader loader = null;

        public Plugin()
        {
            id = new Guid();
            //Bind from attribute.
            PluginData data = PluginData.GetPluginData(this);
            name = data.Name;
            version = data.Version;
            author = data.Author;
            description = data.Description;
            dependencies = data.Dependencies;
        }

        public abstract void OnPluginLoaded(PluginLoader loader);
        public virtual void OnPluginUnloaded(PluginLoader loader) { }
        public virtual void OnPluginEnabled() { }
        public virtual void OnPluginDisabled() { }
        public virtual void OnPluginToggled() { }

        public void Enable()
        {
            OnPluginEnabled();
            toggled = true;
        }

        public void Disable()
        {
            toggled = false;
            OnPluginDisabled();
        }

        public void Toggle()
        {
            OnPluginToggled();
            if (toggled)
                Disable();
            else
                Enable();
        }

        public void Reload()
        {
            loader.UnloadPlugin(this);
            loader.LoadPlugin(pluginFile);
        }

        internal void SetFile(FileInfo pluginFile) => this.pluginFile = pluginFile;
        internal void SetLoader(PluginLoader loader) => this.loader = loader;

        public string GetPluginName() => name;
        public string GetPluginAuthor() => author;
        public string GetPluginDescription() => description;
        public Version GetPluginVersion() => version;
        public Guid GetPluginID() => id;
        public string[] GetPluginDependencies() => dependencies;
        internal FileInfo GetPluginFile() => pluginFile;
        internal PluginLoader GetPluginLoader() => loader;
        internal DirectoryInfo GetPluginDataDirectory() => new DirectoryInfo($"{GetPluginFile().Directory.FullName}\\{name}");
    }

    public sealed class PluginData : Attribute
    {
        private string name = "";
        private string author = "";
        private string description = "";
        private Version version = null;
        private string[] dependencies = new string[0];

        public PluginData(string name, Version version)
        {
            this.name = name;
            this.version = version;
        }

        public PluginData(string name, Version version, string description)
        {
            this.name = name;
            this.version = version;
            this.description = description;

        }

        public string Name
        {
            get => name;
            set
            {
                if (value == null || value == "")
                    name = "";
                else
                    name = value;
            }
        }
        public string Author
        {
            get => author;
            set
            {
                if (value == "" || value == null)
                    author = "";
                else
                    author = value;
            }
        }
        public string Description
        {
            get => description;
            set
            {
                if (value == "" || value == null)
                    description = "";
                else
                    description = value;
            }
        }
        public Version Version
        {
            get => version;
            set
            {
                if (value == null)
                    version = new Version(0, 1);
                else
                    version = value;
            }
        }
        public string[] Dependencies
        {
            get => dependencies;
            set
            {
                if (value.Length == 0 || value == null)
                    dependencies = new string[0];
                else
                    dependencies = value;
            }
        }

        public static PluginData GetPluginData(object obj)
        {
            if (!(obj == null))
            {
                object[] attributes = obj.GetType().GetCustomAttributes(typeof(PluginData), true);
                PluginData plugData = (PluginData)attributes.FirstOrDefault();
                return plugData;
            }
            return null;
        }
    }

    public interface IPluginLoader
    {
        IReadOnlyList<Plugin> GetPlugins();
        void LoadPlugins();
        void UnloadPlugins();
    }

    public sealed class PluginLoader : IPluginLoader
    {
        private List<Plugin> plugins = null;

        public PluginLoader(int maxPlugins = 5000)
        {
            if (maxPlugins <= 0)
                plugins = new List<Plugin>();
            else
                plugins = new List<Plugin>(maxPlugins);
        }

        public IReadOnlyList<Plugin> GetPlugins() => plugins;

        public void LoadPlugins()
        {
            //Check if plugin directory exists.
            if (!(PluginDirectory.Exists))
                PluginDirectory.Create();
            else
            {
                //Load plugins.
                FileInfo[] files = PluginDirectory.GetFiles(".dll", SearchOption.TopDirectoryOnly);
                foreach (FileInfo file in files)
                {
                    LoadPluginFile(file);
                }

                foreach (Plugin plugin in plugins)
                {
                    if (RequiresDependencies(plugin))
                    {
                        foreach (string s in plugin.GetPluginDependencies())
                        {
                            var plug = GetPluginByName(s);
                            if (plug == null) throw new DependencyNotFoundException(plugin);
                            else
                                continue;
                        }
                        //Load Reguardless.
                        plugin.OnPluginLoaded(this);
                        plugin.Enable();
                        //Create plugin data directory.
                        if (!(plugin.GetPluginDataDirectory().Exists))
                            plugin.GetPluginDataDirectory().Create();
                    }
                    else
                    {
                        plugin.OnPluginLoaded(this);
                        plugin.Enable();
                        //Create plugin data directory.
                        if (!(plugin.GetPluginDataDirectory().Exists))
                            plugin.GetPluginDataDirectory().Create();
                    }
                }
            }
        }

        public void LoadPlugin(FileInfo file)
        {
            Type pType = typeof(Plugin);
            Plugin plugin = null;
            Assembly assembly = Assembly.LoadFrom(file.FullName);
            if (assembly == null) return;
            else
            {
                Type[] types = assembly.GetExportedTypes();
                foreach (Type t in types)
                {
                    if (!(t.IsClass) && t.IsNotPublic)
                        continue;

                    if (t.IsAssignableFrom(pType))
                    {
                        plugin = Activator.CreateInstance(t) as Plugin;
                        plugin.SetFile(file);
                        plugin.SetLoader(this);
                    }
                }
            }

            if (RequiresDependencies(plugin))
            {
                foreach (string s in plugin.GetPluginDependencies())
                {
                    var plug = GetPluginByName(s);
                    if (plug == null) throw new DependencyNotFoundException(plugin);
                    else
                        continue;
                }
                //Load Reguardless.
                plugin.OnPluginLoaded(this);
                plugin.Enable();
                //Create plugin data directory.
                if (!(plugin.GetPluginDataDirectory().Exists))
                    plugin.GetPluginDataDirectory().Create();
            }
            else
            {
                plugin.OnPluginLoaded(this);
                plugin.Enable();
                //Create plugin data directory.
                if (!(plugin.GetPluginDataDirectory().Exists))
                    plugin.GetPluginDataDirectory().Create();
            }
        }

        private void LoadPluginFile(FileInfo f)
        {
            Type pType = typeof(Plugin);
            Assembly assembly = Assembly.LoadFrom(f.FullName);
            if (assembly == null)
                return;
            else
            {
                Type[] types = assembly.GetExportedTypes();
                foreach (Type t in types)
                {
                    if (!(t.IsClass) && t.IsNotPublic)
                        continue;
                    
                    if (t.IsAssignableFrom(pType))
                    {
                        Plugin plugin = Activator.CreateInstance(t) as Plugin;
                        plugin.SetFile(f);
                        plugin.SetLoader(this);
                        if (plugin != null)
                            plugins.Add(plugin);
                    }
                }
            }
        }

        public void UnloadPlugin(Plugin plugin)
        {
            if (plugins.Contains(plugin))
            {
                plugin.OnPluginUnloaded(this);
                plugin.Disable();
                plugins.Remove(plugin);
            }
        }

        public void UnloadPlugins()
        {
            foreach (Plugin p in plugins)
            {
                p.OnPluginUnloaded(this);
                p.Disable();
                plugins.Remove(p);
            }
        }

        public void ReloadPlugins()
        {
            UnloadPlugins();
            LoadPlugins();
        }

        public Plugin GetPluginByName(string name)
        {
            foreach (Plugin p in plugins)
                if (p.GetPluginName().Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    return p;
            return null;
        }

        public Plugin GetPluginByID(Guid ID)
        {
            foreach (Plugin p in plugins)
                if (p.GetPluginID() == ID)
                    return p;
            return null;

        }

        public Plugin GetPluginByID(string ID) => GetPluginByID(Guid.Parse(ID));

        public Plugin GetPlugin(string value)
        {
            var pname = GetPluginByName(value);
            var pid = GetPluginByID(value);
            if (pname != null && pid == null)
                return pname;
            else if (pname == null && pid != null)
                return pid;
            else if (pname != null && pid != null)
                return pid;
            else
                return null;
        }

        public bool RequiresDependencies(Plugin p)
        {
            if (p.GetPluginDependencies().Length <= 0 || (p.GetPluginDependencies() == null))
                return false;
            return true;
        }

        static readonly DirectoryInfo WorkingDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        public readonly DirectoryInfo PluginDirectory = new DirectoryInfo(WorkingDirectory.FullName + "\\Plugins");
    }

    public abstract class PluginLoaderException : Exception
    {
        public PluginLoaderException() : base() {}
        public PluginLoaderException(string message) : base(message) { }
        public PluginLoaderException(string message, Exception ex) : base(message, ex) { }
    }

    public sealed class DependencyNotFoundException : PluginLoaderException
    {
        public DependencyNotFoundException(Plugin p) : base($"The specified plugin \"{p.GetPluginName()}\", with ID: \"{p.GetPluginID()}\", could not be found or it was not loaded.") { }
        public DependencyNotFoundException(Plugin p, Exception exception) : base($"The specified plugin \"{p.GetPluginName()}\", with ID: \"{p.GetPluginID()}\", could not be found or it was not loaded.", exception) { }
    }
}

namespace PluginLoader.Configuration
{
    public class Config
    {
        
    }

    public sealed class ConfigManager
    {
        private FileInfo info = null;
        private ConfigMode mode = ConfigMode.JSON;

        internal ConfigManager(FileInfo info, ConfigMode mode)
        {
            this.info = info;
            this.mode = mode;
        }

        public void WriteConfig(Config c)
        {
            if (mode == ConfigMode.JSON)
            {
                using (StreamWriter writer = new StreamWriter(info.FullName, true))
                {
                    var x = ConfigUtils.JSON.WriteJSON(c);
                    writer.Write($"{ReadText()}\n{x}");
                    writer.Flush();
                    writer.Close();
                }
            }
            else if (mode == ConfigMode.YAML)
            {
                using (StreamWriter writer = new StreamWriter(info.FullName, true))
                {
                    var x = ConfigUtils.YAML.WriteYAML(c);
                    writer.Write($"{ReadText()}\n{x}");
                    writer.Flush();
                    writer.Close();
                }

            }
            else
            {
                using (StreamWriter writer = new StreamWriter(info.FullName, true))
                {
                    var x = ConfigUtils.XML.WriteXML(c);
                    writer.Write($"{ReadText()}\n{x}");
                    writer.Flush();
                    writer.Close();
                }
            }
        }

        public Config ReadConfig()
        {
            if (mode == ConfigMode.JSON)
            {
                var x = ReadText();
                return ConfigUtils.JSON.ReadJSON<Config>(x);
            }
            else if (mode == ConfigMode.YAML)
            {
                var x = ReadText();
                return ConfigUtils.YAML.ReadYAML<Config>(x);
            }
            else
            {
                var x = ReadText();
                return ConfigUtils.XML.ReadXML<Config>(x);
            }
        }

        private string ReadText()
        {
            using (StreamReader reader = new StreamReader(info.FullName))
            {
                return reader.ReadToEnd();
            }
        }

        public static ConfigManager CreateConfig(Plugin plugin, ConfigMode mode = ConfigMode.JSON)
        {
            if (mode == ConfigMode.JSON)
            {
                string path = $"{plugin.GetPluginDataDirectory().FullName}\\{plugin.GetPluginName()}.json";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.YAML)
            {
                string path = $"{plugin.GetPluginDataDirectory().FullName}\\{plugin.GetPluginName()}.yaml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.XML)
            {
                string path = $"{plugin.GetPluginDataDirectory().FullName}\\{plugin.GetPluginName()}.xml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else return null;
        }
        public static ConfigManager CreateConfig(Plugin plugin, string name, ConfigMode mode = ConfigMode.JSON)
        {
            if (mode == ConfigMode.JSON)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{name}.json";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.YAML)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{name}.yaml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.XML)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{name}.xml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else return null;
        }
        public static ConfigManager CreateConfig(Plugin plugin, string subFolder ,string name, ConfigMode mode = ConfigMode.JSON)
        {
            if (mode == ConfigMode.JSON)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{subFolder}\\{name}.json";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.YAML)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{subFolder}\\{name}.yaml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else if (mode == ConfigMode.XML)
            {
                string path = $"{plugin.GetPluginDataDirectory()}\\{subFolder}\\{name}.xml";
                FileInfo info = new FileInfo(path);
                if (!(info.Exists))
                    info.Create();
                return new ConfigManager(info, mode);
            }
            else return null;
        }
    }

    public enum ConfigMode
    {
        JSON,
        YAML,
        XML
    }
}
