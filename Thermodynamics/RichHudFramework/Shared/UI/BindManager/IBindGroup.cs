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


            bool DoesBindExist(string name);


            bool DoesComboConflict(IReadOnlyList<ControlHandle> newCombo, IBind currentBind = null, int alias = 0);


            bool DoesComboConflict(IReadOnlyList<int> newConIDs, IBind currentBind = null, int alias = 0);


            bool TryLoadBindData(IReadOnlyList<BindDefinition> bindData);


            void RegisterBinds(BindGroupInitializer bindData);


            void RegisterBinds(IReadOnlyList<string> bindNames);


            IBind GetBind(string name);


            IBind AddBind(string bindName, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null);


            IBind AddBind(string bindName, IReadOnlyList<int> newConIDs, IReadOnlyList<IReadOnlyList<int>> aliases = null);


            bool TryRegisterBind(string bindName, out IBind newBind);


            bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<int> combo, IReadOnlyList<IReadOnlyList<int>> aliases = null);


            bool TryRegisterBind(string bindName, out IBind newBind, IReadOnlyList<ControlHandle> combo, IReadOnlyList<IReadOnlyList<ControlHandle>> aliases = null);


            BindDefinition[] GetBindDefinitions();


            BindDefinitionData[] GetBindData();


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