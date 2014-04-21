using System;
using System.Globalization;
using System.Xml;
using JetBrains.Annotations;
using JetBrains.ReSharper.TaskRunnerFramework;

namespace XunitContrib.Runner.ReSharper.RemoteRunner
{
    [Serializable]
    public class XunitTestClassTask : RemoteTask, IEquatable<XunitTestClassTask>
    {
        public XunitTestClassTask(string projectId, string typeName, bool explicitly)
            : base(XunitTaskRunner.RunnerId)
        {
            if (projectId == null)
                throw new ArgumentNullException("projectId");
            if (typeName == null)
                throw new ArgumentNullException("typeName");

            ProjectId = projectId;
            TypeName = typeName;
            Explicitly = explicitly;
        }

        // This constructor is used to rehydrate a task from an xml element. This is
        // used by the remote test runner's IsolatedAssemblyTestRunner, which creates
        // an app domain to isolate the test assembly from the remote process framework.
        // That framework retrieves these tasks from devenv/resharper via remoting (hence
        // the SerializableAttribute) but uses this hand rolled xml serialisation to
        // get the tasks into the app domain that will actually run the tests
        [UsedImplicitly]
        public XunitTestClassTask(XmlElement element) : base(element)
        {
            ProjectId = GetXmlAttribute(element, AttributeNames.ProjectId);
            TypeName = GetXmlAttribute(element, AttributeNames.TypeName);
            Explicitly = bool.Parse(GetXmlAttribute(element, AttributeNames.Explicitly));
        }

        // ProjectId is for the pathological case where we have tests with the same type name
        // (including namespace). E.g. all of the xunitcontrib tests for xunit 1.9, xunit 1.8, ...
        public string ProjectId { get; private set; }
        public string TypeName { get; private set; }
        public bool Explicitly { get; private set; }

        public override void SaveXml(XmlElement element)
        {
            base.SaveXml(element);
            SetXmlAttribute(element, AttributeNames.ProjectId, ProjectId);
            SetXmlAttribute(element, AttributeNames.TypeName, TypeName);
            SetXmlAttribute(element, AttributeNames.Explicitly, Explicitly.ToString(CultureInfo.InvariantCulture));
        }

        public bool Equals(XunitTestClassTask other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(ProjectId, other.ProjectId) &&
                   Equals(TypeName, other.TypeName) &&
                   Explicitly == other.Explicitly;
        }

        public override bool Equals(RemoteTask other)
        {
            return Equals(other as XunitTestClassTask);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as XunitTestClassTask);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var result = (ProjectId != null ? ProjectId.GetHashCode() : 0);
                result = (result * 397) ^ (TypeName != null ? TypeName.GetHashCode() : 0);
                result = (result * 397) ^ Explicitly.GetHashCode();
                return result;
            }
        }

        public override bool IsMeaningfulTask
        {
            get { return true; }
        }

        public override string ToString()
        {
            return string.Format("XunitTestClassTask<{0}>({1})", Id, TypeName);
        }
    }
}