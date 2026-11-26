using System.Xml.Linq;
using ArchScada.Desktop.Controls.ThreeD;
using Xunit;

namespace ArchScada.Desktop.Tests;

public sealed class ThreeDSyncTests
{
    private static string ResolveRepoPath(params string[] relativeParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine([current, .. relativeParts]);
            if (File.Exists(candidate)) return candidate;
            if (Directory.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current) break;
            current = parent.FullName;
        }
        return Path.GetFullPath(Path.Combine([Directory.GetCurrentDirectory(), .. relativeParts]));
    }

    [Fact]
    public void ThreeDNodes_LinkedScadaIds_MustExistInBaseXmlDevices()
    {
        // 1. Load Device.xml from ArchCoalPlant project
        var deviceXmlPath = ResolveRepoPath("Projects", "ArchCoalPlant", "BaseXML", "Device.xml");
        Assert.True(File.Exists(deviceXmlPath), $"Device.xml not found at {deviceXmlPath}");

        var doc = XDocument.Load(deviceXmlPath);
        var deviceCodes = doc.Descendants("Device")
            .Select(d => d.Element("Code")?.Value)
            .OfType<string>()
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static string MapScadaIdToDeviceCode(string scadaId)
        {
            string normalized = scadaId.Replace("-", "", StringComparison.Ordinal);
            return normalized.ToUpperInvariant() switch
            {
                "AF001" => "ROM101",
                "SL601" => "CV_DRY",
                "DM301" or "MS301" => "TK301",
                "CV501" => "DS_WASH",
                _ => normalized
            };
        }

        // 2. Check all machine nodes
        var machineNodes = Plant3DSceneData.MachineNodes;
        var missingMachines = new List<string>();

        foreach (var node in machineNodes)
        {
            if (!string.IsNullOrEmpty(node.LinkedScadaId))
            {
                string normalizedId = MapScadaIdToDeviceCode(node.LinkedScadaId);
                if (!deviceCodes.Contains(normalizedId))
                {
                    missingMachines.Add($"{node.Id} (Linked: {node.LinkedScadaId}, Normalized: {normalizedId})");
                }
            }
        }

        // 3. Check all conveyors
        var conveyors = Plant3DSceneData.Conveyors;
        var missingConveyors = new List<string>();

        foreach (var conv in conveyors)
        {
            if (!string.IsNullOrEmpty(conv.LinkedScadaId))
            {
                string normalizedId = MapScadaIdToDeviceCode(conv.LinkedScadaId);
                if (!deviceCodes.Contains(normalizedId))
                {
                    missingConveyors.Add($"{conv.Id} (Linked: {conv.LinkedScadaId}, Normalized: {normalizedId})");
                }
            }
        }

        // Assertions
        var allMissing = missingMachines.Concat(missingConveyors).ToList();
        Assert.True(allMissing.Count == 0, 
            $"Found 3D nodes with LinkedScadaIds missing from Device.xml:\n{string.Join("\n", allMissing)}");
    }

    [Fact]
    public void BaseXmlDevices_ShouldHaveCorresponding3DRepresentation()
    {
        var deviceXmlPath = ResolveRepoPath("Projects", "ArchCoalPlant", "BaseXML", "Device.xml");
        var doc = XDocument.Load(deviceXmlPath);
        
        var deviceCodes = doc.Descendants("Device")
            .Select(d => d.Element("Code")?.Value)
            .OfType<string>()
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();

        var sceneLinkedIds = Plant3DSceneData.InteractiveNodes
            .SelectMany(n => new[] { n.Id?.Replace("-", ""), n.LinkedScadaId?.Replace("-", "") })
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        static string MapDeviceCodeToScadaId(string deviceCode)
        {
            return deviceCode.ToUpperInvariant() switch
            {
                "ROM101" => "AF001",
                "CR101" or "DRC101" => "CR101",
                "SC102" => "SC102",
                "CV101" => "CV101",
                "CV102" => "CV102",
                "CV_DRY" => "SL601",
                "DS_WASH" => "CV501",
                "TK201" => "TK201",
                "PP201" => "PP201",
                "PP202" => "PP202",
                "PP203" => "PP203",
                "TK301" => "TK301",
                "CF501" => "CF501",
                "FP501" => "FP501",
                "LD601" => "SL601",
                _ => deviceCode
            };
        }

        var missingInScene = new List<string>();
        foreach (var code in deviceCodes)
        {
            if (code.StartsWith("ENV", StringComparison.OrdinalIgnoreCase) ||
                code.StartsWith("BELT_RIP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "AN601", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string mapped = MapDeviceCodeToScadaId(code);
            if (!sceneLinkedIds.Contains(mapped) && !sceneLinkedIds.Contains(code))
            {
                missingInScene.Add($"{code} (Mapped: {mapped})");
            }
        }

        Assert.True(missingInScene.Count == 0,
            $"Found Device.xml codes with no corresponding 3D node representation:\n{string.Join("\n", missingInScene)}");
    }

    [Fact]
    public void ExportedWebGlbModels_MustMatchMasterCountAndStayUnderWebBudget()
    {
        var masterDir = ResolveRepoPath("ArchScada", "Assets", "3D", "Exported");
        var webDir = ResolveRepoPath("ArchScada", "Assets", "3D", "ExportedWeb");
        Assert.True(Directory.Exists(webDir), $"ExportedWeb directory not found at {webDir}");

        var masterNames = Directory.GetFiles(masterDir, "*.glb").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var webFiles = Directory.GetFiles(webDir, "*.glb").OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(24, webFiles.Count);
        Assert.Equal(masterNames, webFiles.Select(Path.GetFileName).ToList());

        long total = 0;
        foreach (var file in webFiles)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length >= 12, $"{file} has a truncated GLB header.");
            Assert.Equal(0x46546C67u, BitConverter.ToUInt32(bytes, 0));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 8));
            total += bytes.Length;
        }

        Assert.True(total < 3L * 1024 * 1024, $"ExportedWeb total {total} bytes must be < 3 MiB.");
    }

    [Fact]
    public void MasterOpenUsdAssembly_MustExistAndReferenceAll111Nodes()
    {
        var usdPath = ResolveRepoPath("ArchScada", "Assets", "3D", "ArchCoalPlant.usda");
        Assert.True(File.Exists(usdPath), $"OpenUSD master assembly not found at {usdPath}");

        var text = File.ReadAllText(usdPath);
        var nodes = Plant3DSceneData.InteractiveNodes;
        Assert.Equal(111, nodes.Count);

        var missing = nodes
            .Select(n => n.Id)
            .Where(id => !text.Contains($"archscada:nodeId = \"{id}\"", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, $"ArchCoalPlant.usda is missing archscada:nodeId entries for: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ExportedGlbModels_MustAllLoadViaSharpGltfWithValidGeometry()
    {
        var meshes = Plant3DSceneData.LoadAllGlbMeshes();
        Assert.True(meshes.Count >= 24, $"Expected at least 24 loaded GLB models, found {meshes.Count}");

        foreach (var (fileName, mesh) in meshes)
        {
            Assert.True(mesh.VertexCount > 0, $"GLB {fileName} loaded with 0 vertices.");
            Assert.True(mesh.TriangleCount > 0, $"GLB {fileName} loaded with 0 triangles.");
            Assert.True(mesh.BoundsMax.X > mesh.BoundsMin.X, $"GLB {fileName} has invalid X bounding box.");
            Assert.True(mesh.BoundsMax.Y > mesh.BoundsMin.Y, $"GLB {fileName} has invalid Y bounding box.");
            Assert.True(mesh.BoundsMax.Z > mesh.BoundsMin.Z, $"GLB {fileName} has invalid Z bounding box.");
        }
    }
}
