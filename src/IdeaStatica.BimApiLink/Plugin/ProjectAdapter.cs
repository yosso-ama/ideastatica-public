﻿using IdeaStatica.BimApiLink.Importers;
using IdeaStatiCa.BimApi;
using IdeaStatiCa.BimImporter;

namespace IdeaStatica.BimApiLink.Plugin
{
	internal class ProjectAdapter : IProject
	{
		private readonly Project _project;
		private readonly IBimApiImporter _bimApiImporter;

		public ProjectAdapter(Project project, IBimApiImporter bimApiImporter)
		{
			_project = project;
			_bimApiImporter = bimApiImporter;
		}

		public string GetBimApiId(int iomId)
			=> _project.GetBimApiId(iomId);

		public IIdeaObject GetBimObject(int id)
		{
			return _bimApiImporter.Get(_project.GetIdentifier(id));
		}

		public int GetIomId(string bimApiId)
			=> _project.GetIomId(bimApiId);

		public int GetIomId(IIdeaObject obj)
			=> _project.GetIomId(obj);

		public IIdeaPersistenceToken GetPersistenceToken(int iomId)
			=> _project.GetPersistenceToken(iomId);
	}
}