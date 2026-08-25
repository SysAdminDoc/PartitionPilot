using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;

namespace PartitionPilot.Tests;

/// <summary>
/// Guards against the crash class fixed in v0.9.22: a WPF dependency property whose
/// metadata sets BindsTwoWayByDefault (ProgressBar.Value, TextBox.Text, Selector.SelectedItem,
/// ToggleButton.IsChecked, ...) bound without an explicit Mode to a get-only view-model
/// property. WPF throws InvalidOperationException when such a binding attaches, which kills
/// the app during window initialization (see PR #1, EndurancePercent).
/// </summary>
public class BindingModeConventionTests
{
    private static readonly Regex BindingPattern = new(
        @"^\{\s*Binding\b(?<body>.*)\}\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // WPF control namespaces searched when resolving a XAML element name to a type.
    private static readonly string[] ControlNamespaces =
    {
        "System.Windows.Controls",
        "System.Windows.Controls.Primitives",
        "System.Windows",
    };

    [Fact]
    public void Xaml_DefaultTwoWayBindings_TargetSettableViewModelProperties()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory.GetFiles(
            Path.Combine(root, "src", "PartitionPilot"), "*.xaml", SearchOption.AllDirectories);
        var wpfAssembly = typeof(System.Windows.Controls.ProgressBar).Assembly;
        var appAssembly = typeof(MainViewModel).Assembly;
        var violations = new List<string>();

        foreach (var file in xamlFiles)
        {
            var viewModelType = ResolveViewModelType(appAssembly, Path.GetFileNameWithoutExtension(file));
            if (viewModelType is null) continue; // no conventional VM (themes, controls, dialogs)

            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in doc.Descendants())
            {
                var controlType = ResolveControlType(wpfAssembly, element.Name.LocalName);
                if (controlType is null) continue;

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.LocalName.Contains('.')) continue; // attached properties
                    var match = BindingPattern.Match(attribute.Value.Trim());
                    if (!match.Success) continue;

                    var body = match.Groups["body"].Value;
                    if (!BindsToDataContext(body)) continue;
                    if (HasSafeExplicitMode(body)) continue;
                    if (!IsTwoWayByDefault(controlType, attribute.Name.LocalName)) continue;

                    var path = ExtractPath(body);
                    if (path is null) continue;

                    var property = ResolvePropertyPath(viewModelType, path);
                    if (property is null) continue; // template/item context or unresolvable path

                    if (property.SetMethod is null || !property.SetMethod.IsPublic)
                    {
                        var line = ((System.Xml.IXmlLineInfo)element).LineNumber;
                        violations.Add(
                            $"{Path.GetRelativePath(root, file)}:{line} " +
                            $"{element.Name.LocalName}.{attribute.Name.LocalName} binds TwoWay-by-default " +
                            $"to read-only {viewModelType.Name}.{path}; add Mode=OneWay or a setter");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Default-TwoWay binding(s) on read-only view-model properties (crashes at startup): " +
            string.Join("; ", violations));
    }

    private static bool BindsToDataContext(string bindingBody) =>
        !bindingBody.Contains("ElementName", StringComparison.Ordinal) &&
        !bindingBody.Contains("RelativeSource", StringComparison.Ordinal) &&
        !bindingBody.Contains("Source=", StringComparison.Ordinal);

    private static bool HasSafeExplicitMode(string bindingBody) =>
        Regex.IsMatch(bindingBody, @"\bMode\s*=\s*(OneWay|OneTime)\b");

    private static string? ExtractPath(string bindingBody)
    {
        var explicitPath = Regex.Match(bindingBody, @"\bPath\s*=\s*(?<p>[^,}]+)");
        if (explicitPath.Success) return explicitPath.Groups["p"].Value.Trim();

        var firstToken = bindingBody.TrimStart().Split(',')[0].Trim();
        if (firstToken.Length == 0 || firstToken.Contains('=')) return null;
        if (firstToken.Contains('[') || firstToken.Contains('(')) return null; // indexers, attached paths
        return firstToken;
    }

    private static bool IsTwoWayByDefault(Type controlType, string propertyName)
    {
        var descriptor = DependencyPropertyDescriptor.FromName(propertyName, controlType, controlType);
        return descriptor?.Metadata is FrameworkPropertyMetadata metadata && metadata.BindsTwoWayByDefault;
    }

    private static Type? ResolveControlType(Assembly wpfAssembly, string elementName)
    {
        foreach (var ns in ControlNamespaces)
        {
            var type = wpfAssembly.GetType($"{ns}.{elementName}");
            if (type is not null && typeof(DependencyObject).IsAssignableFrom(type)) return type;
        }
        return null;
    }

    private static Type? ResolveViewModelType(Assembly appAssembly, string viewName)
    {
        var baseName = viewName.EndsWith("View", StringComparison.Ordinal)
            ? viewName[..^"View".Length]
            : viewName.EndsWith("Window", StringComparison.Ordinal)
                ? viewName[..^"Window".Length]
                : viewName;
        return appAssembly.GetTypes().FirstOrDefault(t =>
            t.IsClass && !t.IsAbstract && t.Name == baseName + "ViewModel");
    }

    private static PropertyInfo? ResolvePropertyPath(Type rootType, string path)
    {
        var currentType = rootType;
        PropertyInfo? property = null;
        foreach (var segment in path.Split('.'))
        {
            property = currentType.GetProperty(
                segment, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (property is null) return null;
            currentType = property.PropertyType;
        }
        return property;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "PartitionPilot")))
                return dir.FullName;
            dir = dir.Parent!;
        }
        throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
