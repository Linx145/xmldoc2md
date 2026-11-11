using System.CommandLine;
using System.Reflection;
using System.Threading.Tasks;
using Markdown;
using XMLDoc2Markdown;
using XMLDoc2Markdown.Linking;
using XMLDoc2Markdown.Members;
using XMLDoc2Markdown.Rendering;
using XMLDoc2Markdown.Utils;

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using XMLDoc2Markdown.Signatures.Modifiers;
using System.Xml.Linq;
using System.Text;
using XMLDoc2Markdown.Signatures;
using XMLDoc2Markdown.XmlDocId;

namespace XMLDoc2Markdown;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Argument<string> srcArgument = new("src")
        {
            Description = "DLL source path"
        };

        Option<string> outputOption = new("--output", "-o")
        {
            Description = "Output directory"
        };

        Option<string> indexPageNameOption = new("--index-page-name")
        {
            Description = "Name of the index page",
            DefaultValueFactory = _ => "index"
        };

        Option<string> examplesPathOption = new("--examples-path")
        {
            Description = "Path to the code examples to insert in the documentation"
        };
        Option<string> summaryPatchesSourceOption = new("--summary-patches")
        {
            Description = "Path to a folder that contains content to be appended to after the summary category for the file of the same type name."
        };

        Option<string> platformOption = new("--platform")
        {
            Description = "Target documentation host. Presets link rewriting and front matter; "
                + "override any of those with --link-extension / --link-prefix / --front-matter.",
            DefaultValueFactory = _ => "plain"
        };
        platformOption.CompletionSources.Add("plain", "github-pages", "jekyll", "gitlab-wiki", "just-the-docs", "docusaurus");

        Option<string> linkExtensionOption = new("--link-extension")
        {
            Description = "Override the link file extension ('md' keeps '.md', 'none' strips it). Defaults to the platform preset."
        };
        linkExtensionOption.CompletionSources.Add("md", "none");

        Option<string> linkPrefixOption = new("--link-prefix")
        {
            Description = "Override the link prefix ('relative' keeps './', 'none' strips it). Defaults to the platform preset."
        };
        linkPrefixOption.CompletionSources.Add("relative", "none");

        Option<bool> backButtonOption = new("--back-button")
        {
            Description = "Add a back button on each page"
        };

        Option<string> memberAccessibilityLevelOption = new("--member-accessibility-level")
        {
            Description = "Minimum accessibility level of members to be documented.",
            DefaultValueFactory = _ => "protected"
        };
        memberAccessibilityLevelOption.CompletionSources.Add("public", "protected", "internal", "private");

        Option<string> structureOption = new("--structure")
        {
            Description = "Documentation structure.",
            DefaultValueFactory = _ => "flat"
        };
        structureOption.CompletionSources.Add("flat", "tree");

        Option<string> frontMatterOption = new("--front-matter")
        {
            Description = "Override the front matter preset emitted for a documentation system. Defaults to the platform preset."
        };
        frontMatterOption.CompletionSources.Add("none", "jekyll", "just-the-docs", "docusaurus");

        Option<string[]> frontMatterFieldOption = new("--front-matter-field")
        {
            Description = "Extra front matter 'key=value' pair, merged on top of the preset (repeatable). Value emitted verbatim."
        };

        Option<bool> ignoreBindgenImportsOption = new("--ignore-bindgen-imports")
        {
            Description = "Exclude bindgen-generated native import files"
        };
        Option<string?> generateSidebarOption = new("--generate-sidebar")
        {
            Description = "Generate a markdown sidebar for the documentation, using the argument as the header."
        };

        RootCommand rootCommand = new(description: "Tool to generate markdown from C# XML documentation.")
        {
            srcArgument,
            outputOption,
            indexPageNameOption,
            examplesPathOption,
            summaryPatchesSourceOption,
            platformOption,
            linkExtensionOption,
            linkPrefixOption,
            backButtonOption,
            memberAccessibilityLevelOption,
            structureOption,
            frontMatterOption,
            frontMatterFieldOption,
            ignoreBindgenImportsOption,
            generateSidebarOption
        };

        rootCommand.SetAction(parseResult =>
        {
            try
            {
                string src = parseResult.GetValue(srcArgument)!;
                string @out = parseResult.GetValue(outputOption) ?? ".";
                string indexPageName = parseResult.GetValue(indexPageNameOption)!;

                // The platform preset supplies link/front-matter defaults; the explicit
                // --link-extension / --link-prefix / --front-matter flags override them.
                Platform platform = parseResult.GetValue(platformOption) switch
                {
                    "github-pages" => Platform.GitHubPages,
                    "jekyll" => Platform.Jekyll,
                    "gitlab-wiki" => Platform.GitlabWiki,
                    "just-the-docs" => Platform.JustTheDocs,
                    "docusaurus" => Platform.Docusaurus,
                    _ => Platform.Plain,
                };
                bool? noExtension = parseResult.GetValue(linkExtensionOption) switch
                {
                    "none" => true,
                    "md" => false,
                    _ => null,
                };
                bool? noPrefix = parseResult.GetValue(linkPrefixOption) switch
                {
                    "none" => true,
                    "relative" => false,
                    _ => null,
                };
                bool ignoreBindgenImports = parseResult.GetValue(ignoreBindgenImportsOption);
                string? generateSidebarIcon = parseResult.GetValue(generateSidebarOption);
                FrontMatterPreset? frontMatter = parseResult.GetValue(frontMatterOption) switch
                {
                    "none" => FrontMatterPreset.None,
                    "jekyll" => FrontMatterPreset.Jekyll,
                    "just-the-docs" => FrontMatterPreset.JustTheDocs,
                    "docusaurus" => FrontMatterPreset.Docusaurus,
                    _ => null,
                };
                PlatformSettings platformSettings = PlatformDefaults.Resolve(platform, noExtension, noPrefix, frontMatter);

                TypeDocumentationOptions options = new()
                {
                    ExamplesDirectory = parseResult.GetValue(examplesPathOption),
                    SummaryPatchesSource = parseResult.GetValue(summaryPatchesSourceOption),
                    NoExtension = platformSettings.NoExtension,
                    NoPrefix = platformSettings.NoPrefix,
                    BackButton = parseResult.GetValue(backButtonOption),
                    MemberAccessibilityLevel = parseResult.GetValue(memberAccessibilityLevelOption) switch
                    {
                        "private" => Accessibility.Private,
                        "internal" => Accessibility.Internal,
                        "protected" => Accessibility.Protected,
                        _ => Accessibility.Public,
                    },
                    Structure = parseResult.GetValue(structureOption) switch
                    {
                        "tree" => DocumentationStructure.Tree,
                        _ => DocumentationStructure.Flat,
                    },
                    FrontMatter = platformSettings.FrontMatter,
                    FrontMatterFields = ParseFrontMatterFields(parseResult.GetValue(frontMatterFieldOption))
                };
                int succeeded = 0;
                int failed = 0;

                Directory.CreateDirectory(@out);

                Assembly assembly = new AssemblyLoadContext(src)
                    .LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(src)));

                string assemblyName = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(src);
                XmlDocumentation documentation = new(src);
                Logger.Info($"Generation started: Assembly: {assemblyName}");

                StringBuilder? sidebar = null;
                if (generateSidebarIcon != null)
                {
                    sidebar = new StringBuilder();
                    sidebar.AppendLine($"<img src=\"{generateSidebarIcon}\"></img>");
                }
                IMarkdownDocument indexPage = new MarkdownDocument().AppendHeader(assemblyName, 1);

                Type?[] loadedTypes;
                try
                {
                    loadedTypes = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Logger.Warning($"Some types failed to load: {ex.LoaderExceptions.Length} loader exception(s).");
                    loadedTypes = ex.Types;
                }

                IEnumerable<TypeContextPair> types = loadedTypes
                    .Where(type => type is not null)
                    .Select(type => type!)
                    .Where(type => (type.IsPublic || type.IsNestedPublic) && !typeof(Delegate).IsAssignableFrom(type))
                    .Select(type => new TypeContextPair(type, new RenderingContext(assembly, type, documentation, options)))
                    .Where(pair => pair.context != null);
                
                IEnumerable<IGrouping<string?, TypeContextPair>> typesByCategory = types.GroupBy(pair =>
                {
                    var memberDocElement = pair.context.GetMemberDoc(pair.type);
                    
                    IEnumerable<XNode>? nodes = memberDocElement?.Element("category")?.Nodes();
                    if (nodes != null)
                    {
                        var first = nodes.FirstOrDefault();
                        if (first is not null and XText text)
                        {
                            return text.Value;
                        }
                    }
                    return pair.type.Namespace;
                }).OrderBy(g => g.Key);

                foreach (IGrouping<string?, TypeContextPair> categorisedTypes in typesByCategory)
                {
                    sidebar?.AppendLine($"- {categorisedTypes.Key}");
                    indexPage.AppendHeader(categorisedTypes.Key ?? "No category", 2);

                    foreach (TypeContextPair pair in categorisedTypes.OrderBy(x => x.type.Name))
                    {
                        Type type = pair.type;
                        if (ignoreBindgenImports && (type.Name.EndsWith("Imports") || type.Name == "ModuleLoadFuncs"))
                        {
                            continue;
                        }

                        string fileName = type.GetDocsFileName(options.Structure);
                        sidebar?.AppendLine($"  - [{type.GetDisplayName(true).FormatChevrons()}](api-reference/{fileName})");
                        Logger.Info($"  {fileName}.md");

                        indexPage.AppendParagraph(type.GetDocsLink(assembly, options.Structure, noExtension: options.NoExtension, noPrefix: options.NoPrefix));

                        try
                        {
                            string filePath = Path.Combine(@out, $"{fileName}.md");
                            string? directory = Path.GetDirectoryName(filePath);

                            if (directory != null)
                            {
                                Directory.CreateDirectory(directory);
                            }

                            File.WriteAllText(
                                filePath,
                                new TypeDocumentation(pair.context).ToString()
                            );
                            succeeded++;
                        }
                        catch (Exception exception)
                        {
                            Logger.Error(exception.Message);
                            failed++;
                        }
                    }
                }

                if (sidebar != null)
                {
                    File.WriteAllText(
                        Path.Combine(@out, "_sidebar.md"),
                        sidebar.ToString());
                }

                File.WriteAllText(
                    Path.Combine(@out, $"{indexPageName}.md"),
                    FrontMatterRenderer.ForIndex(assemblyName, indexPageName, options) + indexPage.ToString());

                Logger.Info($"Generation: {succeeded} succeeded, {failed} failed");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Unable to generate documentation:");
                Logger.Error(ex.Message);
                return 1;
            }
        });

        return await rootCommand.Parse(args).InvokeAsync();

        static List<KeyValuePair<string, string>> ParseFrontMatterFields(string[]? raw)
        {
            List<KeyValuePair<string, string>> fields = [];
            foreach (string entry in raw ?? [])
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0)
                {
                    Logger.Warning($"Ignoring malformed --front-matter-field '{entry}' (expected key=value).");
                    continue;
                }

                fields.Add(new(entry[..separator].Trim(), entry[(separator + 1)..]));
            }

            return fields;
        }
    }
}