using System;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
    namespace UI
    {
        using Server;
        using Client;

        using ControlContainerMembers = MyTuple<
            ApiMemberAccessor,
            MyTuple<object, Func<int>>,
            object
        >;

        public enum TerminalAccessors : int
        {
            ToggleMenu = 0,
            OpenMenu = 1,
            CloseMenu = 2,
            OpenToPage = 3,
            SetPage = 4,
            GetMenuOpen = 5,

			GetNewPageCategoryFunc = 6
        }

        public enum MenuControls : int
        {
            Checkbox = 1,
            ColorPicker = 2,
            OnOffButton = 3,
            SliderSetting = 4,
            TerminalButton = 5,
            TextField = 6,
            DropdownControl = 7,
            ListControl = 8,
            DragBox = 9,
            Label = 10,
        }

        public enum ControlContainers : int
        {
            Tile = 1,
            Category = 2,
        }

        public enum ModPages : int
        {
            ControlPage = 1,
            RebindPage = 2,
            TextPage = 3,
        }

        public enum ModControlRootAccessors : int
        {
            GetOrSetCallback = 1,

            GetCategoryAccessors = 7,

            AddSubcategory = 8
        }

        public interface IModRootMember
        {
            string Name { get; set; }

            bool Enabled { get; set; }

            object ID { get; }
        }

		public interface IModControlRoot : ITerminalPageCategory
        {
            event EventHandler SelectionChanged;

            IReadOnlyList<TerminalPageCategoryBase> Subcategories { get; }


            void Add(TerminalPageCategoryBase subcategory);


            void AddRange(IReadOnlyList<IModRootMember> members);


            ControlContainerMembers GetApiData();
        }
    }
}