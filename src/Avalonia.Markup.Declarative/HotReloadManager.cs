using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Avalonia.Markup.Declarative.HotReloadManager))]

namespace Avalonia.Markup.Declarative;

/// <summary>Tracks declarative views and reloads them when their types change during hot reload.</summary>
public static class HotReloadManager
{
    private static readonly ConcurrentDictionary<Type, ConditionalWeakTable<IReloadable, object?>> Instances = new();

    /// <summary>Raised after application updates have been applied to tracked views.</summary>
    public static event Action<Type[]?>? HotReloaded;

    /// <summary>Gets whether automatic view reload is enabled.</summary>
    public static bool IsEnabled { get; private set; } = true;

    /// <summary>Enables automatic view reload after application updates.</summary>
    public static void Activate() =>
        IsEnabled = true;

    /// <summary>Disables automatic view reload after application updates.</summary>
    public static void Deactivate() =>
        IsEnabled = false;

    private static void OnHotReloaded(Type[]? types) =>
        HotReloaded?.Invoke(types);

    /// <summary>Called by the runtime when metadata changes are applied.</summary>
    /// <param name="types">The types changed by the update, or <see langword="null"/> when unspecified.</param>
    public static void ClearCache(Type[]? types)
    {
        Log("ClearCache for types: " + PrintTypes(types));
    }

    /// <summary>Reloads tracked views affected by an application update and raises <see cref="HotReloaded"/>.</summary>
    /// <param name="types">The types changed by the update, or <see langword="null"/> when unspecified.</param>
    public static void UpdateApplication(Type[]? types)
    {
        if (IsEnabled)
            ReloadInstances(types);

        OnHotReloaded(types);
    }

    private static void ReloadInstances(Type[]? types)
    {
        Log("UpdateApplication for types: " + PrintTypes(types));
        if (types == null)
            return;

        foreach (var type in types)
        {
            if (!Instances.TryGetValue(type, out var table))
                continue;

            foreach (var instance in table)
            {
                instance.Key.Reload();
            }
        }
    }

    [Conditional("DEBUG")]
    private static void Log(string message) => Debug.WriteLine($"[Markup.HotReload] {message}");

    /// <summary>Formats the supplied types as a comma-separated list of type names.</summary>
    /// <param name="types">The types to format, or <see langword="null"/>.</param>
    /// <returns>The comma-separated type names, or an empty string when <paramref name="types"/> is null.</returns>
    public static string PrintTypes(Type[]? types)
    {
        if (types != null)
        {
            return string.Join(", ", types.Select(t => t.Name));
        }

        return "";
    }

    /// <summary>
    /// Enumerates the live attached instances currently tracked for hot reload. Used by the
    /// diagnostics component registry to list active views.
    /// </summary>
    internal static IEnumerable<IReloadable> GetLiveInstances()
    {
        foreach (var table in Instances.Values)
        {
            foreach (var pair in table)
                yield return pair.Key;
        }
    }

    internal static void RegisterInstance(IReloadable instance)
    {
        if (!IsEnabled) return;
        var type = instance.GetType();
        if (type.IsGenericType) type = type.GetGenericTypeDefinition();

        var table = Instances.GetOrAdd(type, _ => []);
        table.AddOrUpdate(instance, null);
    }

    internal static void UnregisterInstance(IReloadable instance)
    {
        if (!IsEnabled) return;

        var type = instance.GetType();

        if (type.IsGenericType)
        {
            type = type.GetGenericTypeDefinition();
        }

        if (!Instances.TryGetValue(type, out var table)) return;

        table.Remove(instance);
    }
}
