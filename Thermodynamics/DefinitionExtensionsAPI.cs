using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Serialization;
using VRage.Utils;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Draygo.BlockExtensionsAPI
{

	public class DefinitionExtensionsAPI
	{
		public const long MODID = 2756894170;
		IReadOnlyDictionary<Type, Delegate> _methods;
		Action m_callback = null;
		bool m_init = false;

		public bool Init
		{
			get
			{
				return m_init;
			}
		}

		public DefinitionExtensionsAPI(Action callback)
		{
			if (MyAPIGateway.Utilities == null)
				MyAPIGateway.Utilities = MyAPIUtilities.Static;

			m_callback = callback;

			MyAPIGateway.Utilities.RegisterMessageHandler(MODID, recieveModHandlers);
		}

		public void UnloadData()
		{
			MyAPIGateway.Utilities.UnregisterMessageHandler(MODID, recieveModHandlers);
		}

		public enum AdditionalMethods : int
		{
			None=0,
			RegisterTSS=1,
			UnRegisterTSS=2,
			RegisterDataTSS = 3,
			GetDataTSS = 4,
			DefIDExists = 5,
			GetGroups = 6,
			GetProperties = 7,
			GetAllIndexedIds = 8
		}

		private void recieveModHandlers(object obj)
		{
			if (Init)
				return;
			try
			{
				if (obj is IReadOnlyDictionary<Type, Delegate>)
				{
					_methods = (IReadOnlyDictionary<Type, Delegate>)obj;
					Assign(typeof(float), ref _floatMethod);
					Assign(typeof(double), ref _doubleMethod);
					Assign(typeof(int), ref _intMethod);
					Assign(typeof(long), ref _longMethod);
					Assign(typeof(string), ref _textMethod);
					Assign(typeof(Color), ref _colorMethod);
					Assign(typeof(bool), ref _booleanMethod);
					Assign(typeof(Vector2I), ref _vector2IMethod);
					Assign(typeof(Vector2D), ref _vector2DMethod);
					Assign(typeof(Vector3I), ref _vector3IMethod);
					Assign(typeof(Vector3D), ref _vector3DMethod);
					Assign(typeof(MyGameLogicComponent), ref _setGameLogic);
					Assign(typeof(Delegate), ref _getDelegate);
					if (_getDelegate != null)
					{
						Assign(AdditionalMethods.RegisterTSS, ref _RegisterTSS);
						Assign(AdditionalMethods.UnRegisterTSS, ref _UnregisterTSS);
						Assign(AdditionalMethods.RegisterDataTSS, ref _RegisterTSSDataComponent);
						Assign(AdditionalMethods.GetDataTSS, ref _GetTSSDataComponent);
						Assign(AdditionalMethods.DefIDExists, ref _DefIDExists);
						Assign(AdditionalMethods.GetGroups, ref _GetGroups);
						Assign(AdditionalMethods.GetProperties, ref _GetProperties);
						Assign(AdditionalMethods.GetAllIndexedIds, ref _GetAllIndexedIds);
					}
					m_init = true;
					m_callback?.Invoke();
				}
			}
			catch (Exception ex)
			{
				MyLog.Default.WriteLine(@"Error - Below crash is caused by the API mod being the incorrect version. Delete steamapps\workshop\content\244850\2756894170 to force a redownload");
				throw ex;
			}
		}

		private void Assign<T>(Type valuetype, ref T method) where T : class
		{
			method = _methods[valuetype] as T;
		}

		private void Assign<T>(AdditionalMethods mt, ref T method) where T : class
		{
			method = _getDelegate((int)mt) as T;
		}

		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, float>> _floatMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, double>> _doubleMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, int>> _intMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, long>> _longMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, string>> _textMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, Color>> _colorMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, bool>> _booleanMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, Vector2I>> _vector2IMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, Vector2D>> _vector2DMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, Vector3I>> _vector3IMethod;
		private Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, Vector3D>> _vector3DMethod;
		private Action<MyStringId, IMyModContext, Func<MyGameLogicComponent>> _setGameLogic;
		private Func<int, Delegate> _getDelegate;
		private Action<MyTSSCommon, IMyTextSurface, IMyTerminalBlock, Action<MyTSSCommon, IMyTerminalBlock, List<IMyTerminalControl>>> _RegisterTSS;
		private Action<MyTSSCommon, IMyTextSurface, IMyTerminalBlock> _UnregisterTSS;
		private Action<Type, Type, IMyModContext, Func<MyEntityComponentBase>> _RegisterTSSDataComponent;
		private Func<Type, IMyTerminalBlock, MyEntityComponentBase> _GetTSSDataComponent;
		private Func<MyDefinitionId, bool> _DefIDExists;
		private Action<MyDefinitionId, List<MyStringId>> _GetGroups;
		private Action<MyDefinitionId, MyStringId, List<MyTuple<MyStringId, Type>>> _GetProperties;
		private Action<HashSet<MyDefinitionId>> _GetAllIndexedIds;
		public bool DefinitionIdExists(MyDefinitionId definition)
		{
			return _DefIDExists?.Invoke(definition) ?? false;
		}


		public void GetAllIndexedIds(HashSet<MyDefinitionId> obj)
		{
			_GetAllIndexedIds?.Invoke(obj);
		}

		public void GetGroups(MyDefinitionId definition, List<MyStringId> grouplist)
		{
			_GetGroups?.Invoke(definition, grouplist);
		}

		public void GetProperties(MyDefinitionId definition, MyStringId groupid, List<MyTuple<MyStringId, Type>> properties)
		{
			_GetProperties?.Invoke(definition, groupid, properties);
		}


		public void RegisterTSS(MyTSSCommon script, IMyTextSurface surface, IMyTerminalBlock block, Action<MyTSSCommon, IMyTerminalBlock, List<IMyTerminalControl>> controlgetter)
		{
			_RegisterTSS?.Invoke(script, surface, block, controlgetter);
		}

		public void UnRegisterTSS(MyTSSCommon script, IMyTextSurface surface, IMyTerminalBlock block)
		{
			_UnregisterTSS?.Invoke(script, surface, block);
		}

		public void RegisterTSSDataComponent<T, U>(IMyModContext modContext, Func<U> customFactory = null) where U : MyEntityComponentBase, new()
			where T : MyTSSCommon
		{
			if (customFactory == null)
				customFactory = () => { return new U(); };

			_RegisterTSSDataComponent?.Invoke(typeof(T), typeof(U), modContext, customFactory);
		}

		public MyEntityComponentBase GetTSSDataComponent<T>(IMyTerminalBlock block) where T : MyTSSCommon
		{
			return _GetTSSDataComponent(typeof(T), block);
		}

		public void RegisterGameLogic<T>(MyStringId componentName, IMyModContext mod, Func<T> customFactory = null) where T : MyGameLogicComponent, new()
		{
			if(customFactory == null)
				customFactory = () => { return new T(); };

			_setGameLogic?.Invoke(componentName, mod, customFactory);
		}

		public bool TryGetText(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out string value)
		{
			var retval = _textMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetString(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out string value)
		{
			return TryGetText(definition, group, propertyname, out value);
		}

		public bool TryGetInt(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out int value)
		{
			var retval = _intMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetLong(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out long value)
		{
			var retval = _longMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetFloat(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out float value)
		{
			var retval = _floatMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetDouble(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out double value)
		{
			var retval = _doubleMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetBool(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out bool value)
		{
			var retval = _booleanMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetColor(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out Color value)
		{
			var retval = _colorMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetVector2I(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out Vector2I value)
		{
			var retval = _vector2IMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetVector2D(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out Vector2D value)
		{
			var retval = _vector2DMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetVector3I(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out Vector3I value)
		{
			var retval = _vector3IMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGetVector3D(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out Vector3D value)
		{
			var retval = _vector3DMethod.Invoke(definition, group, propertyname);
			value = retval.Item2;
			return retval.Item1;
		}

		public bool TryGet<T>(MyDefinitionId definition, MyStringId group, MyStringId propertyname, out T value)
		{
			if(!_methods?.ContainsKey(typeof(T)) ?? false)
			{
				value = default(T);
				return false;
			}
			var result = ((Func<MyDefinitionId, MyStringId, MyStringId, MyTuple<bool, T>>)_methods[typeof(T)]).Invoke(definition, group, propertyname);
			value = result.Item2;
			return result.Item1;
		}
	}
}
