using System;
using System.Reflection;
using System.Linq;

try {
    var assemblies = new[] { "ModelContextProtocol.Core", "ModelContextProtocol" };
    foreach (var asmName in assemblies) {
        Console.WriteLine($"=== Assembly: {asmName} ===");
        try {
            var assembly = Assembly.Load(asmName);
            var types = assembly.GetTypes().OrderBy(t => t.FullName).ToList();
            foreach (var type in types) {
                if (type.IsPublic) {
                    Console.WriteLine($"  Type: {type.FullName}");
                    // Print some methods/properties if interesting
                    if (type.Name.Contains("Server") || type.Name.Contains("Tool") || type.Name.Contains("Mcp")) {
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) {
                            if (method.DeclaringType == type) {
                                Console.WriteLine($"    Method: {method.Name}");
                            }
                        }
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading assembly {asmName}: {ex.Message}");
        }
    }
} catch (Exception ex) {
    Console.WriteLine($"Fatal Error: {ex.Message}");
}

