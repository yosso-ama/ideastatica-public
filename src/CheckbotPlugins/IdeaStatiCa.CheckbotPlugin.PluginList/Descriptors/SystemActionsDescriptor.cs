﻿namespace IdeaStatiCa.PluginSystem.PluginList.Descriptors
{
	public sealed class SystemActionsDescriptor
	{
		public ActionButtonDescriptor? Open { get; }

		public SystemActionsDescriptor(ActionButtonDescriptor? open)
		{
			Open = open;
		}
	}
}