﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using Sdl.Web.Common;
using Sdl.Web.Common.Configuration;
using Sdl.Web.Common.Interfaces;
using Sdl.Web.Common.Logging;
using Sdl.Web.Common.Models;
using Sdl.Web.DataModel;
using Sdl.Web.Tridion.ContentManager;
using Sdl.Web.Tridion.Mapping;
using Sdl.Web.Tridion.Statics;
using Tridion.ContentDelivery.DynamicContent;
using Tridion.ContentDelivery.DynamicContent.Query;

namespace Sdl.Web.Tridion.R2Mapping
{
    /// <summary>
    /// Default Content Provider implementation (based on DXA R2 Data Model).
    /// </summary>
    public class DefaultContentProviderR2 : IContentProvider, IRawDataProvider
    {
        /// <summary>
        /// Gets a Page Model for a given URL path.
        /// </summary>
        /// <param name="urlPath">The URL path (unescaped).</param>
        /// <param name="localization">The context <see cref="Localization"/>.</param>
        /// <param name="addIncludes">Indicates whether include Pages should be expanded.</param>
        /// <returns>The Page Model.</returns>
        /// <exception cref="DxaItemNotFoundException">If no Page Model exists for the given URL.</exception>
        public PageModel GetPageModel(string urlPath, Localization localization, bool addIncludes = true)
        {
            using (new Tracer(urlPath, localization, addIncludes))
            {
                string canonicalUrlPath = GetCanonicalUrlPath(urlPath);

                string pageContent = GetPageContent(canonicalUrlPath, localization);
                if (pageContent == null)
                {
                    // This may be a SG URL path; try if the index page exists.
                    canonicalUrlPath += Constants.IndexPageUrlSuffix;
                    pageContent = GetPageContent(canonicalUrlPath, localization);
                    if (pageContent == null)
                    {
                        throw new DxaItemNotFoundException(urlPath, localization.LocalizationId);
                    }
                }

                PageModelData pageModelData = JsonConvert.DeserializeObject<PageModelData>(pageContent, DataModelBinder.SerializerSettings);

                string pageUri = localization.GetCmUri(pageModelData.Id, (int) ItemType.Page);
                List<string> dependencies = new List<string>() { pageUri };
                // TODO: add include Page TCM URIs to dependencies

                PageModel result = null;
                if (CacheRegions.IsViewModelCachingEnabled)
                {
                    PageModel cachedPageModel = SiteConfiguration.CacheProvider.GetOrAdd(
                        $"{pageUri}:{addIncludes}", // Cache Page Models with and without includes separately
                        CacheRegions.PageModel,
                        () =>
                        {
                            PageModel pageModel = ModelBuilderPipelineR2.CreatePageModel(pageModelData, addIncludes, localization);
                            pageModel.Url = canonicalUrlPath; // TODO: generate canonical Page URL on CM-side (?)
                            if (pageModel.NoCache)
                            {
                                result = pageModel;
                                return null;
                            }
                            return pageModel;
                        },
                        dependencies
                        );

                    if (cachedPageModel != null)
                    {
                        // Don't return the cached Page Model itself, because we don't want dynamic logic to modify the cached state.
                        result = (PageModel) cachedPageModel.DeepCopy();
                    }
                }
                else
                {
                    result = ModelBuilderPipelineR2.CreatePageModel(pageModelData, addIncludes, localization);
                    result.Url = canonicalUrlPath;  // TODO: generate canonical Page URL on CM-side (?)
                }

                if (SiteConfiguration.ConditionalEntityEvaluator != null)
                {
                    result.FilterConditionalEntities(localization);
                }

                return result;
            }
        }

        /// <summary>
        /// Gets an Entity Model for a given Entity Identifier.
        /// </summary>
        /// <param name="id">The Entity Identifier. Must be in format {ComponentID}-{TemplateID}.</param>
        /// <param name="localization">The context Localization.</param>
        /// <returns>The Entity Model.</returns>
        /// <exception cref="DxaItemNotFoundException">If no Entity Model exists for the given URL.</exception>
        public EntityModel GetEntityModel(string id, Localization localization)
        {
            using (new Tracer(id, localization))
            {
                string[] idParts = id.Split('-');
                if (idParts.Length != 2)
                {
                    throw new DxaException($"Invalid Entity Identifier '{id}'. Must be in format ComponentID-TemplateID.");
                }

                string componentUri = localization.GetCmUri(idParts[0]);
                string templateUri = localization.GetCmUri(idParts[1], (int) ItemType.ComponentTemplate);

                ComponentPresentationFactory cpFactory = new ComponentPresentationFactory(componentUri);
                ComponentPresentation dcp = cpFactory.GetComponentPresentation(componentUri, templateUri);
                if (dcp == null)
                {
                    throw new DxaItemNotFoundException(id, localization.Id);
                }

                EntityModelData entityModelData = JsonConvert.DeserializeObject<EntityModelData>(dcp.Content, DataModelBinder.SerializerSettings);

                EntityModel result;
                if (CacheRegions.IsViewModelCachingEnabled)
                {
                    EntityModel cachedEntityModel = SiteConfiguration.CacheProvider.GetOrAdd(
                        $"{id}-{localization.Id}", // key
                        CacheRegions.EntityModel,
                        () => ModelBuilderPipelineR2.CreateEntityModel(entityModelData, typeof(EntityModel), localization),
                        dependencies: new[] { componentUri }
                        );

                    // Don't return the cached Entity Model itself, because we don't want dynamic logic to modify the cached state.
                    result = (EntityModel) cachedEntityModel.DeepCopy();
                }
                else
                {
                    result = ModelBuilderPipelineR2.CreateEntityModel(entityModelData, typeof(EntityModel), localization);
                }

                if (result.XpmMetadata != null)
                {
                    // Entity Models requested through this method are per definition "query based" in XPM terminology.
                    result.XpmMetadata["IsQueryBased"] = true; // TODO TSI-24: Do this in Model Service (or CM-side?)
                }

                return result;
            }
        }

        /// <summary>
        /// Gets a Static Content Item for a given URL path.
        /// </summary>
        /// <param name="urlPath">The URL path (unescaped).</param>
        /// <param name="localization">The context Localization.</param>
        /// <returns>The Static Content Item.</returns>
        public StaticContentItem GetStaticContentItem(string urlPath, Localization localization)
        {
            using (new Tracer(urlPath, localization))
            {
                string localFilePath = BinaryFileManager.Instance.GetCachedFile(urlPath, localization);

                return new StaticContentItem(
                    new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan),
                    MimeMapping.GetMimeMapping(localFilePath),
                    File.GetLastWriteTime(localFilePath),
                    Encoding.UTF8
                    );
            }
        }

        public void PopulateDynamicList(DynamicList dynamicList, Localization localization)
        {
            throw new NotImplementedException(); // TODO TSI-1265
        }

        string IRawDataProvider.GetPageContent(string urlPath, Localization localization)
            => GetPageContent(urlPath, localization);

        private static string GetCanonicalUrlPath(string urlPath)
        {
            string result = urlPath ?? Constants.IndexPageUrlSuffix;
            if (!result.StartsWith("/"))
            {
                result = "/" + result;
            }
            if (result.EndsWith("/"))
            {
                result += Constants.DefaultExtensionLessPageName;
            }
            else if (result.EndsWith(Constants.DefaultExtension))
            {
                result = result.Substring(0, result.Length - Constants.DefaultExtension.Length);
            }
            return result;
        }

        private static string GetPageContent(string urlPath, Localization localization)
        {
            using (new Tracer(urlPath, localization))
            {
                if (!urlPath.EndsWith(Constants.DefaultExtension) && !urlPath.EndsWith(".json"))
                {
                    urlPath += Constants.DefaultExtension;
                }

                string escapedUrlPath = Uri.EscapeUriString(urlPath);

                global::Tridion.ContentDelivery.DynamicContent.Query.Query brokerQuery = new global::Tridion.ContentDelivery.DynamicContent.Query.Query
                {
                    Criteria = CriteriaFactory.And(new Criteria[]
                    {
                        new PageURLCriteria(escapedUrlPath),
                        new PublicationCriteria(Convert.ToInt32(localization.Id)),
                        new ItemTypeCriteria(64)
                    })
                };


                string[] pageUris = brokerQuery.ExecuteQuery();
                if (pageUris.Length == 0)
                {
                    return null;
                }
                if (pageUris.Length > 1)
                {
                    throw new DxaException($"Broker Query for Page URL path '{urlPath}' in Publication '{localization.Id}' returned {pageUris.Length} results.");
                }

                PageContentAssembler pageContentAssembler = new PageContentAssembler();
                return pageContentAssembler.GetContent(pageUris[0]);
            }
        }
    }
}