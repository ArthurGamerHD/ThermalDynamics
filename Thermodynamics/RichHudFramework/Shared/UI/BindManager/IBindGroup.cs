using System.Collections.Generic;
using VRage;

namespace RichHudFramework
{
    using BindDefinitionData = MyTuple<string, string[], string[][]>;

    namespace UI
    {
        using Client;
        using Server;

		public interface IBindGroup : IReadOnlyList<IBind>
        {
            IBind this[string name] { get; }

            string Name { get; }

            int Index { get; }

            object ID { get; }

/// <summary>DoesBindExist operation.</summary>
            bool DoesBindExist(string name);

/// <summary>DoesComboConflict operation.</summary>
            bool DoesComboConflict(IReadOnlyList<ControlHandle> newCombo, IBind currentBind = null, int alias = 0);

/// <summary>DoesComboConflict operation.</summary>
            bool DoesComboConflict(IReadOnlyList<int> newConIDs, IBind currentBind = null, int alias = 0);

/// <summary>TryLoadBindData operation.</summary>
            bool TryLoadBindData(IReadOnlyList<BindDefinition> bindData);

/// <summary>Registers the API and message handler.</summary>
            void RegisterBinds(BindGroupInitializer bindData);

/// <summary>Registers the API and message handler.</summary>
            void RegisterBinds(IReadOnlyList<string> bindNames);

/// <summary>Returns the bind.</summary>
            IBind GetBind(string name);

/// <summary>Adds a bind.</summary>
            IBind AddBind(string bindName, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null);

/// <summary>Adds a bind.</summary>
            IBind AddBind(string bindName, IReadOnlyList<int> newConIDs, IReadOnlyList<IReadOnlyList<int>> aliases = null);

/// <summary>TryRegisterBind operation.</summary>
            bool TryRegisterBind(string bindName, out IBind newBind);

/// <summary>TryRegisterBind operation.</summary>
            bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<int> combo, IReadOnlyList<IReadOnlyList<int>> aliases = null);

/// <summary>TryRegisterBind operation.</summary>
            bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null);

/// <summary>Returns the binddefinitions.</summary>
            BindDefinition[] GetBindDefinitions();

/// <summary>Returns the binddata.</summary>
            BindDefinitionData[] GetBindData();

/// <summary>ClearSubscribers operation.</summary>
            void ClearSubscribers();
        }

        public enum BindGroupAccessors : int
        {
            Name = 1,

            ID = 2,

            DoesComboConflict = 3,

            TryRegisterBindName = 4,

            TryRegisterBindWithIndices = 5,

            TryRegisterBindWithNames = 6,

            TryLoadBindData = 7,

            GetBindData = 8,

            DoesBindExist = 9,

            GetBindFromName = 10,

            RegisterBindNames = 11,

            RegisterBindIndices = 12,

            RegisterBindDefinitions = 13,

            AddBindWithIndices = 14,

            AddBindWithNames = 15,

            ClearSubscribers = 16,
        }
    }
}