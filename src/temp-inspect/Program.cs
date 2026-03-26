using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// Check AgentSession public props and StateBag access
var t = typeof(AgentSession);
Console.WriteLine("AgentSession public props:");  
foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine("  " + p.PropertyType.Name + " " + p.Name);
Console.WriteLine("AgentSession public methods:");
foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    Console.WriteLine("  " + m.Name);
