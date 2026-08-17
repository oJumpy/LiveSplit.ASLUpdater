using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;

[assembly: ComponentFactory(typeof(LiveSplit.ASLUpdater.src.Factory))]

namespace LiveSplit.ASLUpdater.src
{
    public class Factory : IComponentFactory
    {
        public string ComponentName => "ASL Updater";
        public string Description => "Automatically checks for and updates loaded ASL auto-splitter scripts from GitHub.";
        public ComponentCategory Category => ComponentCategory.Other;
        public string UpdateName => ComponentName;
        public string XMLURL => "";
        public string UpdateURL => "";
        public Version Version => Version.Parse("1.0.0");

        public IComponent Create(LiveSplitState state)
        {
            return new Component(state);
        }
    }
}