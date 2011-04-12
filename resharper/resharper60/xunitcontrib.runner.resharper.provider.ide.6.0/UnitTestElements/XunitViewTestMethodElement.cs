using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.TaskRunnerFramework.UnitTesting;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using XunitContrib.Runner.ReSharper.UnitTestRunnerProvider.TestElements;

namespace XunitContrib.Runner.ReSharper.UnitTestProvider
{
    internal class XunitViewTestMethodElement : XunitTestMethodElement, IUnitTestViewElement, ISerializableUnitTestElement
    {
        private readonly IProjectModelElementPointer projectPointer;

        public XunitViewTestMethodElement(IUnitTestRunnerProvider provider, XunitViewTestClassElement testClass, IProject project, string id, string typeName, string methodName, bool isSkip)
            : base(provider, testClass, id, typeName, methodName, isSkip)
        {
            Class = testClass;
            projectPointer = project.CreatePointer();
        }

        public void WriteToXml(XmlElement parent)
        {
            parent.SetAttribute("id", Id);
            parent.SetAttribute("projectId", GetProject().GetPersistentID());
            parent.SetAttribute("typeName", TypeName);
            parent.SetAttribute("methodName", MethodName);
        }

        internal static IUnitTestViewElement ReadFromXml(XmlElement parent, IUnitTestViewElement parentElement, XunitTestProvider provider)
        {
            var testClass = parentElement as XunitViewTestClassElement;
            if (testClass == null)
                throw new InvalidOperationException("parentElement should be xUnit.net test class");

            var id = parent.GetAttribute("id");
            var typeName = parent.GetAttribute("typeName");
            var methodName = parent.GetAttribute("methodName");
            var projectId = parent.GetAttribute("projectId");

            var project = (IProject)ProjectUtil.FindProjectElementByPersistentID(provider.Solution, projectId);
            if (project == null)
                return null;

            return provider.GetOrCreateTestMethod(id, project, testClass, typeName, methodName, false);
        }

        public bool Equals(IUnitTestViewElement other)
        {
            return Equals(other as XunitViewTestMethodElement);
        }

        public bool Equals(XunitViewTestMethodElement other)
        {
            return other != null && base.Equals(other) && other.TypeName == TypeName &&
                   other.MethodName == MethodName;
        }

        public IProject GetProject()
        {
            return (projectPointer.GetValidProjectElement(((XunitTestProvider)Provider).Solution) as IProject);
        }

        public IProjectModelElementPointer GetProjectPointer()
        {
            return projectPointer;
        }

        public string GetTitle()
        {
            // TODO: return methodname, or Class.MethodName if Class != TypeName
            return MethodName;
        }

        public UnitTestNamespace GetNamespace()
        {
            return new UnitTestNamespace(new ClrTypeName(TypeName).GetNamespaceName());
        }

        public UnitTestElementDisposition GetDisposition()
        {
            var element = GetDeclaredElement();
            if (element == null || !element.IsValid())
                return UnitTestElementDisposition.InvalidDisposition;

            var locations = from declaration in element.GetDeclarations()
                            let file = declaration.GetContainingFile()
                            where file != null
                            select new UnitTestElementLocation(file.GetSourceFile().ToProjectFile(),
                                                               declaration.GetNameDocumentRange().TextRange,
                                                               declaration.GetDocumentRange().TextRange);
            return new UnitTestElementDisposition(locations, this);
        }

        public IDeclaredElement GetDeclaredElement()
        {
            var declaredType = GetDeclaredType();
            if (declaredType == null)
                return null;

            foreach (var member in declaredType.EnumerateMembers(MethodName, declaredType.CaseSensistiveName))
            {
                var method = member as IMethod;
                // TODO: Really?
                if (method != null && !method.IsAbstract && method.TypeParameters.Count <= 0 
                    && (method.AccessibilityDomain.DomainType == AccessibilityDomain.AccessibilityDomainType.PUBLIC || method.AccessibilityDomain.DomainType == AccessibilityDomain.AccessibilityDomainType.INTERNAL))
                {
                    return member;
                }
            }

            return null;
        }

        private ITypeElement GetDeclaredType()
        {
            var p = GetProject();
            if (p == null)
                return null;

            var provider = (XunitTestProvider) Provider;
            var psiModule = provider.PsiModuleManager.GetPrimaryPsiModule(p);
            if (psiModule == null)
                return null;

            return provider.CacheManager.GetDeclarationsCache(psiModule, true, true).GetTypeElementByCLRName(TypeName);
        }

        public string Kind
        {
            get { return "xUnit.net Test"; }
        }

        public IEnumerable<UnitTestElementCategory> Categories
        {
            get { return Enumerable.Empty<UnitTestElementCategory>(); }
        }

        public string ExplicitReason
        {
            get { return string.Empty; }
        }

        internal XunitViewTestClassElement Class { get; private set; }
    }
}