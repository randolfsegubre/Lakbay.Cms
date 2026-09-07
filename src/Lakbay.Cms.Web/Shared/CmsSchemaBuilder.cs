using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Lakbay.Cms.Web.Shared;

/// <summary>
/// Code-first document-type/data-type creation, shared between the
/// Products tree (<c>Catalog.CatalogContentTypeSeeder</c>) and the
/// Content tree (<c>Content.ContentTreeSeeder</c>) — extracted once a
/// second seeder needed the exact same "look up a built-in data type,
/// create one on demand if missing, build a content type with a Content
/// tab" logic, rather than copy-pasting it a second time.
/// </summary>
public sealed class CmsSchemaBuilder(
    IContentTypeService contentTypeService,
    IDataTypeService dataTypeService,
    IShortStringHelper shortStringHelper,
    PropertyEditorCollection propertyEditors,
    IConfigurationEditorJsonSerializer configurationEditorJsonSerializer)
{
    /// <summary>
    /// A fresh Umbraco 18.1.1 install pre-creates a data type for some
    /// built-in property editors (e.g. "Textstring" for Umbraco.TextBox)
    /// but not all of them — confirmed the hard way: Umbraco.Decimal has
    /// none by default. Looking up by editor alias first avoids hard-
    /// coding a display name that isn't part of any documented contract;
    /// falling back to creating one (default configuration, the editor's
    /// own `DefaultConfiguration`) covers whichever editors aren't
    /// pre-seeded, without needing to know in advance which ones those are.
    /// </summary>
    public async Task<IDataType> GetBuiltInDataTypeAsync(string propertyEditorAlias)
    {
        var matches = await dataTypeService.GetByEditorAliasAsync(propertyEditorAlias);
        var existing = matches.FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        if (!propertyEditors.TryGet(propertyEditorAlias, out var editor) || editor is null)
        {
            throw new InvalidOperationException(
                $"Property editor '{propertyEditorAlias}' is not registered in this Umbraco installation.");
        }

        var dataType = new DataType(editor, configurationEditorJsonSerializer, -1)
        {
            Name = $"Lakbay {propertyEditorAlias}",
        };

        var result = await dataTypeService.CreateAsync(dataType, Constants.Security.SuperUserKey);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create a data type for '{propertyEditorAlias}': {result.Status}");
        }

        return result.Result;
    }

    public ContentType NewContentType(string alias, string name, string icon, bool isElement = false, bool allowedAsRoot = true) =>
        new(shortStringHelper, -1)
        {
            Alias = alias,
            Name = name,
            Icon = icon,
            IsElement = isElement,
            AllowedAsRoot = allowedAsRoot,
        };

    public void AddProperties(ContentType contentType, params (IDataType DataType, string Alias, string Name)[] properties)
    {
        var group = new PropertyGroup(new PropertyTypeCollection(contentType.SupportsPublishing))
        {
            Alias = "content",
            Name = "Content",
            Type = PropertyGroupType.Tab,
        };

        foreach (var (dataType, alias, name) in properties)
        {
            group.PropertyTypes!.Add(new PropertyType(shortStringHelper, dataType)
            {
                Alias = alias,
                Name = name,
            });
        }

        contentType.PropertyGroups.Add(group);
    }

    public async Task SaveNewAsync(IContentType contentType)
    {
        var result = await contentTypeService.CreateAsync(contentType, Constants.Security.SuperUserKey);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create content type '{contentType.Alias}': {result.Result}");
        }
    }

}
