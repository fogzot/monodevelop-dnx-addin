﻿//
// DnxProjectService.cs
//
// Author:
//       Matt Ward <ward.matt@gmail.com>
//
// Copyright (c) 2015 Matthew Ward
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using Microsoft.CodeAnalysis;
using Microsoft.Framework.DesignTimeHost.Models.OutgoingMessages;
using MonoDevelop.Dnx.Omnisharp;
using MonoDevelop.Core;
using MonoDevelop.Ide;
using MonoDevelop.Projects;
using OmniSharp.Dnx;
using OmniSharp.Models;

namespace MonoDevelop.Dnx
{
	public class DnxProjectService
	{
		DnxContext context;
		DnxProjectSystem projectSystem;
		MonoDevelopApplicationLifetime applicationLifetime;
		DnxProjectBuilder builder;
		string initializeError = String.Empty;

		public DnxProjectService ()
		{
		}

		public void Initialize ()
		{
			IdeApp.Workspace.SolutionLoaded += SolutionLoaded;
			IdeApp.Workspace.SolutionUnloaded += SolutionUnloaded;
			IdeApp.Workspace.ActiveConfigurationChanged += ActiveConfigurationChanged;
			FileService.FileChanged += FileChanged;
		}

		void SolutionUnloaded (object sender, SolutionEventArgs e)
		{
			UnloadProjectSystem ();
		}

		void UnloadProjectSystem ()
		{
			initializeError = String.Empty;
			if (applicationLifetime != null) {
				applicationLifetime.Stopping ();
				applicationLifetime.Dispose ();
				applicationLifetime = null;
				if (projectSystem != null) {
					projectSystem.Dispose ();
					projectSystem = null;
				}
				context = null;
			}
		}

		void SolutionLoaded (object sender, SolutionEventArgs e)
		{
			OnSolutionLoaded (e.Solution);
		}

		internal void OnSolutionLoaded (Solution solution)
		{
			try {
				if (solution.HasDnxProjects ()) {
					LoadDnxProjectSystem (solution);
				}
			} catch (Exception ex) {
				LoggingService.LogError ("DNX project system initialize failed.", ex);
				UnloadProjectSystem ();
				initializeError = "Unable to initialize DNX project system. " + ex.Message;
				OnProjectSystemFailed ();
			}
		}

		public event EventHandler ProjectSystemLoadFailed;

		void OnProjectSystemFailed ()
		{
			var handler = ProjectSystemLoadFailed;
			if (handler != null) {
				handler (this, new EventArgs ());
			}
		}

		void LoadDnxProjectSystem (Solution solution)
		{
			UnloadProjectSystem ();

			applicationLifetime = new MonoDevelopApplicationLifetime ();
			context = new DnxContext ();
			var factory = new DnxProjectSystemFactory ();
			projectSystem = factory.CreateProjectSystem (solution, applicationLifetime, context);
			projectSystem.Initalize ();

			if (context.RuntimePath == null) {
				string error = GetRuntimeError (projectSystem);
				throw new ApplicationException (error);
			}
		}

		static string GetRuntimeError (DnxProjectSystem projectSystem)
		{
			if (projectSystem.DnxPaths != null &&
				projectSystem.DnxPaths.RuntimePath != null &&
				projectSystem.DnxPaths.RuntimePath.Error != null) {
				return projectSystem.DnxPaths.RuntimePath.Error.Text;
			}
			return "Unable to find DNX runtime.";
		}

		public void OnReferencesUpdated (ProjectId projectId, FrameworkProject frameworkProject)
		{
			DispatchService.GuiDispatch (()  => {
				var locator = new DnxProjectLocator (context);
				DnxProject project = locator.FindProject (projectId);
				if (project != null) {
					project.UpdateReferences (frameworkProject);
				}
			});
		}

		public void OnProjectChanged (OmniSharp.Models.DnxProject project)
		{
			DispatchService.GuiDispatch (() => UpdateProject (project));
		}

		void UpdateProject (OmniSharp.Models.DnxProject project)
		{
			DnxProject matchedProject = FindProjectByProjectJsonFileName (project.Path);
			if (matchedProject != null) {
				matchedProject.Update (project);
			}
		}

		public DnxRuntime CurrentDnxRuntime {
			get {
				if (context != null) {
					return new DnxRuntime (context.RuntimePath);
				}
				return null;
			}
		}

		public bool HasCurrentDnxRuntime {
			get { return context != null; }
		}

		public bool HasCurrentRuntimeError {
			get { return !String.IsNullOrEmpty (initializeError); }
		}

		public string CurrentRuntimeError {
			get { return initializeError; }
		}

		public void DependenciesUpdated (OmniSharp.Dnx.Project project, DependenciesMessage message)
		{
			DispatchService.GuiDispatch (() => UpdateDependencies (project, message));
		}

		static DnxProject FindProjectByProjectJsonFileName (string fileName)
		{
			Solution solution = IdeApp.ProjectOperations.CurrentSelectedSolution;
			if (solution == null)
				return null;

			DnxProject project = solution.FindProjectByProjectJsonFileName (fileName);
			if (project != null) {
				return project;
			}

			LoggingService.LogWarning (String.Format("Unable to find project by json file. '{0}'", fileName));
			return null;
		}

		void UpdateDependencies (OmniSharp.Dnx.Project project, DependenciesMessage message)
		{
			DnxProject matchedProject = FindProjectByProjectJsonFileName (project.Path);
			if (matchedProject != null) {
				matchedProject.UpdateDependencies (message);
			}
		}

		public void PackageRestoreStarted (string projectJsonFileName)
		{
			DispatchService.GuiDispatch (() => OnPackageRestoreStarted (projectJsonFileName));
		}

		void OnPackageRestoreStarted (string projectJsonFileName)
		{
			DnxProject matchedProject = FindProjectByProjectJsonFileName(projectJsonFileName);
			if (matchedProject != null) {
				matchedProject.OnPackageRestoreStarted ();
			}
		}

		public void PackageRestoreFinished (string projectJsonFileName)
		{
			DispatchService.GuiDispatch (() => OnPackageRestoreFinished(projectJsonFileName));
		}

		void OnPackageRestoreFinished(string projectJsonFileName)
		{
			DnxProject matchedProject = FindProjectByProjectJsonFileName (projectJsonFileName);
			if (matchedProject != null) {
				matchedProject.OnPackageRestoreFinished ();
			}
		}

		public void OnParseOptionsChanged (ProjectId projectId, ParseOptions options)
		{
			DispatchService.GuiDispatch (()  => {
				var locator = new DnxProjectLocator (context);
				DnxProject project = locator.FindProject (projectId);
				if (project != null) {
					project.UpdateParseOptions (locator.FrameworkProject, options);
				}
			});
		}

		void ActiveConfigurationChanged (object sender, EventArgs e)
		{
			if (projectSystem == null)
				return;

			string config = IdeApp.Workspace.ActiveConfigurationId;
			if (config != null) {
				projectSystem.ChangeConfiguration (config);
			}
		}

		public void GetDiagnostics (DnxProjectBuilder builder)
		{
			this.builder = builder;
			projectSystem.GetDiagnostics (builder.ProjectPath);
		}

		public void ReportDiagnostics (OmniSharp.Dnx.Project project, DiagnosticsMessage[] messages)
		{
			if (builder != null) {
				builder.OnDiagnostics (messages);
				builder = null;
			}
		}

		void FileChanged (object sender, FileEventArgs e)
		{
			Solution solution = IdeApp.ProjectOperations.CurrentSelectedSolution;
			if (solution == null)
				return;

			if (!solution.HasDnxProjects ())
				return;

			if (!IsGlobalJsonFileChanged (e))
				return;

			OnSolutionLoaded (solution);
		}

		static bool IsGlobalJsonFileChanged (FileEventArgs e)
		{
			foreach (FileEventInfo fileInfo in e) {
				if (fileInfo.FileName.FileName.Equals ("global.json", StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			}

			return false;
		}

		static readonly string DnxRestoreProperty = "MonoDevelop.Dnx.RestoreDependencies";

		public bool RestoreDependencies {
			get { return PropertyService.Get (DnxRestoreProperty, true); }
			set {
				PropertyService.Set (DnxRestoreProperty, value);
				OnRestoreDependenciesChanged (value);
			}
		}

		void OnRestoreDependenciesChanged (bool value)
		{
			if (projectSystem != null) {
				projectSystem.Options.Dnx.EnablePackageRestore = value;
			}
		}
	}
}

