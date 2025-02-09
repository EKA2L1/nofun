﻿using Nofun.Services;
using Nofun.Services.Unity;
using Nofun.UI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Nofun
{
    [DefaultExecutionOrder(-1)]
    public class EmulatorLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<ScreenManager>();
            builder.RegisterComponentInHierarchy<LayoutService>().AsImplementedInterfaces();
            builder.RegisterComponentInHierarchy<DialogService>().AsImplementedInterfaces();
            builder.RegisterComponentInHierarchy<DocumentStackManager>();

            builder.Register<ITranslationService, TranslationService>(Lifetime.Scoped);
        }
    }
}
