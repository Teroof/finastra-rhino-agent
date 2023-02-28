﻿/*
 * CHANGE LOG - keep only last 5 threads
 * 
 * RESSOURCES
 */
using Gravity.Services.Comet.Engine.Attributes;

using Microsoft.CodeAnalysis;

using Rhino.Api.Contracts.AutomationProvider;
using Rhino.Api.Parser;
using Rhino.Controllers.Extensions;
using Rhino.Controllers.Models;
using Rhino.Controllers.Models.Server;
using Rhino.Settings;

using System.Collections.Concurrent;
using System.Runtime.Serialization;

namespace Rhino.Controllers.Domain.Cache
{
    /// <summary>
    /// Contract for caching meta data.
    /// </summary>
    [DataContract]
    public static class MetaDataCache
    {
        // members: cache state
        private static readonly AppSettings s_appSettings = new();
        private static IDictionary<string, PluginsCacheModel> s_plugins = GetPluginsCache(Utilities.Types);

        #region *** Singleton(s) ***
        [DataMember]
        public static IDictionary<string, PluginsCacheModel> Plugins
        {
            get
            {
                s_plugins ??= GetPluginsCache(Utilities.Types);
                return s_plugins;
            }
        }
        #endregion

        #region *** Plugins: Get ***
        // TODO: add from external repository
        // TODO: load assemblies domain from folder for Gravity plugins
        private static IDictionary<string, PluginsCacheModel> GetPluginsCache(IEnumerable<Type> types)
        {
            // setup
            var cache = new ConcurrentDictionary<string, PluginsCacheModel>(StringComparer.OrdinalIgnoreCase);
            var factory = new RhinoPluginFactory();
            var rootDirectory = Path.Combine(Environment.CurrentDirectory, "Plugins"/*Build dynamically from configuration*/);
            var directories = !Directory.Exists(rootDirectory)
                ? Array.Empty<string>()
                : Directory
                    .GetDirectories(rootDirectory)
                    .Where(i => Path.GetFileName(i)
                    .StartsWith("Rhino", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            // get plugins: code
            var gravityPlugins = types.GetActionAttributes();

            // get plugins: rhino
            var plugins = new List<(string Source, RhinoPlugin Plugin)>();
            foreach(var directory in directories)
            {
                var repository = GetPluginsRepository(directory).ToArray();
                foreach (var item in factory.GetRhinoPlugins(repository))
                {
                    plugins.Add((directory, item));
                }
            }

            // build cache
            foreach (var group in plugins.GroupBy(i => i.Source))
            {
                var groupCollection = new List<PluginCacheModel>();
                var key = Path.GetFileName(group.Key);
                foreach (var item in group.Select(i => (Path: i.Source, i.Plugin, Attribute: i.Plugin.ToAttribute())))
                {
                    var cacheModel = GetPluginCacheModel(ActionModel.ActionSource.Plugin, item.Path, item.Plugin, item.Attribute);
                    groupCollection.Add(cacheModel);
                }
                cache[key] = new()
                {
                    ActionsCache = groupCollection.Select(i => i.ActionModel).OrderBy(i => i.Key),
                    ActionsCacheByConfiguration = Array.Empty<ActionModel>(),
                    PluginsCache = groupCollection
                };
            }
            cache["Gravity"] = new()
            {
                ActionsCache = gravityPlugins.Select(i => new ActionModel
                {
                    Entity = (ActionAttribute)i,
                    Key = i.Name,
                    Literal = i.Name.ToSpaceCase().ToLower(),
                    Source = ActionModel.ActionSource.Code,
                    Verb = "TBD"
                }).OrderBy(i => i.Key),
                ActionsCacheByConfiguration = Array.Empty<ActionModel>(),
                PluginsCache = gravityPlugins.Select(i => GetPluginCacheModel((ActionAttribute)i))
            };

            // get
            return cache;
        }

        // TODO: encrypt/decrypt plugins body
        private static IEnumerable<string> GetPluginsRepository(string inDirectory)
        {
            // setup
            var encryptionKey = s_appSettings.StateManager?.DataEncryptionKey ?? string.Empty;

            // setup conditions
            var exists = Directory.Exists(inDirectory);

            // NotFound conditions
            if (!exists)
            {
                return Array.Empty<string>();
            }

            // collect plugins
            return Directory
                .GetDirectories(inDirectory)
                .SelectMany(Directory.GetFiles)
                .Select(File.ReadAllText);
        }

        private static PluginCacheModel GetPluginCacheModel(ActionAttribute attribute)
        {
            return GetPluginCacheModel(
                source: ActionModel.ActionSource.Code,
                path: default,
                plugin: default,
                attribute);
        }

        private static PluginCacheModel GetPluginCacheModel(string source, string path, RhinoPlugin plugin, ActionAttribute attribute)
        {
            // setup
            var actionModel = new ActionModel
            {
                Entity = attribute,
                Key = attribute.Name,
                Literal = attribute.Name.ToSpaceCase().ToLower(),
                Source = source,
                Verb = "TBD"
            };

            // get
            return new()
            {
                ActionModel = actionModel,
                Directory = Path.GetFileName(path),
                Path = Path.Exists(path) ? path : null,
                Plugin = plugin,
                Specifications = plugin == default ? null : plugin?.ToString()
            };
        }
        #endregion
    }
}
