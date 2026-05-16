// using System.Collections.Generic;
// using System.Globalization;
// using System.Linq;
// using Microsoft.Extensions.Configuration;
// using YamlDotNet.Serialization;
// using YamlDotNet.Serialization.NamingConventions;
//
// namespace MicroClaw.Configuration;
//
// /// <summary>
// /// Binds an <see cref="IConfiguration"/> section onto a strongly typed options instance
// /// by reconstructing a YAML-equivalent object tree from the configuration nodes and
// /// feeding it to YamlDotNet's <see cref="IDeserializer"/>.
// /// <para>
// /// Sharing the same <see cref="UnderscoredNamingConvention"/> and <c>[YamlMember]</c>
// /// </para>
// /// </summary>
// internal static class YamlAwareBinder
// {
//     private static readonly ISerializer NodeSerializer = new SerializerBuilder()
//         .WithNamingConvention(NullNamingConvention.Instance) // keys are already final
//         .Build();
//
//     private static readonly IDeserializer Deserializer = new DeserializerBuilder()
//         .WithNamingConvention(UnderscoredNamingConvention.Instance)
//         .IgnoreUnmatchedProperties()
//         .Build();
//
//     public static void Bind(IConfiguration section, object instance)
//     {
//         ArgumentNullException.ThrowIfNull(section);
//         ArgumentNullException.ThrowIfNull(instance);
//
//         object? tree = BuildNode(section);
//         if (tree is null)
//             return;
//
//         // Round-trip through YAML so YamlDotNet handles aliases, conventions, scalar conversion, etc.
//         string yaml = NodeSerializer.Serialize(tree);
//         object? bound = Deserializer.Deserialize(yaml, instance.GetType());
//         if (bound is null)
//             return;
//
//         CopyProperties(bound, instance);
//     }
//
//     /// <summary>
//     /// Converts an <see cref="IConfiguration"/> subtree into a plain CLR object graph:
//     /// <list type="bullet">
//     /// <item>leaf with <c>Value</c> → <see cref="string"/></item>
//     /// <item>children whose keys are 0,1,2,... → <see cref="List{T}"/></item>
//     /// <item>otherwise → <see cref="Dictionary{TKey,TValue}"/> keyed by child key</item>
//     /// </list>
//     /// </summary>
//     private static object? BuildNode(IConfiguration node)
//     {
//         IConfigurationSection[] children = node.GetChildren().ToArray();
//
//         if (children.Length == 0)
//         {
//             return node is IConfigurationSection leaf ? leaf.Value : null;
//         }
//
//         if (IsList(children))
//         {
//             List<object?> list = new(children.Length);
//             foreach (IConfigurationSection child in children
//                          .OrderBy(c => int.Parse(c.Key, CultureInfo.InvariantCulture)))
//             {
//                 list.Add(BuildNode(child));
//             }
//             return list;
//         }
//
//         Dictionary<string, object?> map = new(children.Length, StringComparer.Ordinal);
//         foreach (IConfigurationSection child in children)
//         {
//             map[child.Key] = BuildNode(child);
//         }
//         return map;
//     }
//
//     private static bool IsList(IConfigurationSection[] children)
//     {
//         for (int i = 0; i < children.Length; i++)
//         {
//             if (!int.TryParse(children[i].Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
//                 return false;
//         }
//         return children.Length > 0;
//     }
//
//     /// <summary>
//     /// Copies public writable properties from <paramref name="source"/> to <paramref name="target"/>.
//     /// Preserves the caller-provided instance identity (DI singletons, IOptions snapshots, etc.).
//     /// </summary>
//     private static void CopyProperties(object source, object target)
//     {
//         var props = target.GetType().GetProperties(
//             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
//
//         foreach (var prop in props)
//         {
//             if (!prop.CanWrite || prop.GetIndexParameters().Length > 0)
//                 continue;
//
//             object? value = prop.GetValue(source);
//             prop.SetValue(target, value);
//         }
//     }
// }