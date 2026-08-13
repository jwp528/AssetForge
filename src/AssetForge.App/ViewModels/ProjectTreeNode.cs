using System.Collections.ObjectModel;
using AssetForge.Core.Models;

namespace AssetForge.App.ViewModels;

public sealed class ProjectTreeNode
{
    public required string Name { get; init; }
    public AssetFile? Asset { get; init; }
    public ObservableCollection<ProjectTreeNode> Children { get; } = [];
    public bool IsFolder => Asset is null;

    public static ObservableCollection<ProjectTreeNode> Build(IEnumerable<AssetFile> assets)
    {
        var roots = new ObservableCollection<ProjectTreeNode>();
        foreach (var asset in assets)
        {
            var current = roots;
            var parts = asset.RelativePath.Replace('\\', '/').Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var folder = current.FirstOrDefault(n => n.IsFolder && string.Equals(n.Name, parts[i], StringComparison.OrdinalIgnoreCase));
                if (folder is null) { folder = new ProjectTreeNode { Name = parts[i] }; current.Add(folder); }
                current = folder.Children;
            }
            current.Add(new ProjectTreeNode { Name = asset.Name, Asset = asset });
        }
        Sort(roots);
        return roots;
    }

    private static void Sort(ObservableCollection<ProjectTreeNode> nodes)
    {
        foreach (var node in nodes) Sort(node.Children);
        var ordered = nodes.OrderByDescending(n => n.IsFolder).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        nodes.Clear(); foreach (var node in ordered) nodes.Add(node);
    }
}
