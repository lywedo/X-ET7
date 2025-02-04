﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bright.Serialization;

namespace ET
{
	/// <summary>
    /// Config组件会扫描所有的有ConfigAttribute标签的配置,加载进来
    /// </summary>
    public class ConfigComponent: Singleton<ConfigComponent>
    {
        public struct GetAllConfigBytes: ICallback
        {
            public int Id { get; set; }
        }
        
        public struct GetOneConfigBytes: ICallback
        {
            public int Id { get; set; }
            public string ConfigName;
        }
		
        private readonly Dictionary<string, IConfigSingleton> allConfig = new Dictionary<string, IConfigSingleton>(20);

		public override void Dispose()
		{
			foreach (var kv in this.allConfig)
			{
				kv.Value.Destroy();
			}
		}

		public object LoadOneConfig(Type configType)
		{
			this.allConfig.TryGetValue(configType.Name, out IConfigSingleton oneConfig);
			if (oneConfig != null)
			{
				oneConfig.Destroy();
			}
			
			ByteBuf oneConfigBytes = EventSystem.Instance.Callback<GetOneConfigBytes, ByteBuf>(new GetOneConfigBytes() {ConfigName = configType.FullName});

			object category = Activator.CreateInstance(configType, oneConfigBytes);
			IConfigSingleton singleton = category as IConfigSingleton;
			singleton.Register();
			
			this.allConfig[configType.Name] = singleton;
			return category;
		}
		
		public void Load()
		{
			this.allConfig.Clear();
			HashSet<Type> types = EventSystem.Instance.GetTypes(typeof (ConfigAttribute));
			
			Dictionary<string, ByteBuf> configBytes = EventSystem.Instance.Callback<GetAllConfigBytes, Dictionary<string, ByteBuf>>(new GetAllConfigBytes());

			foreach (Type type in types)
			{
				this.LoadOneInThread(type, configBytes);
			}
			
			foreach (IConfigSingleton category in this.allConfig.Values)
			{
				category.Register();
				category.Resolve(allConfig);
			}
		}
		
		public async ETTask LoadAsync()
		{
			this.allConfig.Clear();
			HashSet<Type> types = EventSystem.Instance.GetTypes(typeof (ConfigAttribute));
			
			Dictionary<string, ByteBuf> configBytes = EventSystem.Instance.Callback<GetAllConfigBytes, Dictionary<string, ByteBuf>>(new GetAllConfigBytes());

			using ListComponent<Task> listTasks = ListComponent<Task>.Create();
			
			foreach (Type type in types)
			{
				Task task = Task.Run(() => LoadOneInThread(type, configBytes));
				listTasks.Add(task);
			}

			await Task.WhenAll(listTasks.ToArray());

			foreach (IConfigSingleton category in this.allConfig.Values)
			{
				category.Register();
				category.Resolve(allConfig);
			}
		}
		
		private void LoadOneInThread(Type configType, Dictionary<string, ByteBuf> configBytes)
		{
			ByteBuf oneConfigBytes = configBytes[configType.Name];

			object category = Activator.CreateInstance(configType, oneConfigBytes);
			
			lock (this)
			{
				this.allConfig[configType.Name] = category as IConfigSingleton;	
			}
		}
		
		public void TranslateText(Func<string, string, string> translator)
		{
			foreach (IConfigSingleton category in this.allConfig.Values)
			{
				category.TranslateText(translator);
			}
		}
	}
}