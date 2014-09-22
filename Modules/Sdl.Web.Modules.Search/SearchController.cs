﻿using Sdl.Web.Common.Interfaces;
using Sdl.Web.Common.Logging;
using Sdl.Web.Common.Models;
using Sdl.Web.Mvc.Controllers;
using System;
using System.Reflection;

namespace Sdl.Web.Modules.Search
{
    public class SearchController : BaseController
    {
        public virtual ISearchProvider SearchProvider { get; set; }
        public SearchController(IContentProvider contentProvider, IRenderer renderer, ISearchProvider searchProvider)
        {
            ContentProvider = contentProvider;
            SearchProvider = searchProvider;
            Renderer = renderer;
        }

        override protected object ProcessModel(object sourceModel, Type type)
        {
            var model = base.ProcessModel(sourceModel, type);
            if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(SearchQuery<>)))
            {
                //Use reflection to execute the generic method ISearchProvider.ExecuteQuery
                //As we do not know the generic type until runtime (its specified by the view model)
                Type resultType = type.GetGenericArguments()[0];
                MethodInfo method = typeof(ISearchProvider).GetMethod("ExecuteQuery");
                MethodInfo generic = method.MakeGenericMethod(resultType);
                return generic.Invoke(SearchProvider, new object[] { Request.Params, model });
            }
            else
            {
                Exception ex = new Exception("Cannot run query - View Model is not of type SearchQuery<T>.");
                Log.Error(ex);
                throw ex;
            }
        }
    }
}
