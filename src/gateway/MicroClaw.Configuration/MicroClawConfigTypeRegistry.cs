using System.Reflection;

namespace MicroClaw.Configuration;

internal static class MicroClawConfigTypeRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<Type> RegisteredTypes = [];

    public static IEnumerable<Type> GetManagedConfigTypes()
    {
        HashSet<Type> managedTypes = [];

        foreach (Type optionType in EnumerateAssemblyTypes(typeof(IMicroClawConfigOptions).Assembly))
        {
            if (IsManagedConfigType(optionType))
                managedTypes.Add(optionType);
        }

        lock (SyncRoot)
        {
            foreach (Type optionType in RegisteredTypes)
            {
                if (IsManagedConfigType(optionType))
                    managedTypes.Add(optionType);
            }
        }

        return managedTypes;
    }

    internal static void RegisterType(Type optionType)
    {
        ArgumentNullException.ThrowIfNull(optionType);

        lock (SyncRoot)
        {
            RegisteredTypes.Add(optionType);
        }
    }

    internal static void ResetForTests()
    {
        lock (SyncRoot)
        {
            RegisteredTypes.Clear();
        }
    }

    private static IEnumerable<Type> EnumerateAssemblyTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null).Cast<Type>();
        }
    }

    private static bool IsManagedConfigType(Type optionType)
    {
        return typeof(IMicroClawConfigOptions).IsAssignableFrom(optionType)
            && optionType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false) is not null;
    }
}