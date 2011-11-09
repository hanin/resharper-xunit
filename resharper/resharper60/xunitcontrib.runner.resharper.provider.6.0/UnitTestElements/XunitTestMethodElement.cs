using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;
using XunitContrib.Runner.ReSharper.RemoteRunner;

namespace XunitContrib.Runner.ReSharper.UnitTestProvider
{
    public class XunitTestMethodElement : IUnitTestElement, ISerializableUnitTestElement
    {
        private readonly ProjectModelElementEnvoy projectModelElementEnvoy;
        private readonly CacheManager cacheManager;
        private readonly PsiModuleManager psiModuleManager;
        private readonly IClrTypeName typeName;
        private readonly string methodName;
        private XunitTestClassElement parent;

        public XunitTestMethodElement(IUnitTestProvider provider, XunitTestClassElement testClass, ProjectModelElementEnvoy projectModelElementEnvoy, CacheManager cacheManager,
            PsiModuleManager psiModuleManager, string id, IClrTypeName typeName, string methodName, string skipReason)
        {
            Provider = provider;
            Parent = testClass;
            this.projectModelElementEnvoy = projectModelElementEnvoy;
            this.cacheManager = cacheManager;
            this.psiModuleManager = psiModuleManager;
            Id = id;
            this.typeName = typeName;
            this.methodName = methodName;
            ExplicitReason = skipReason;
            State = UnitTestElementState.Valid;
        }

        public IProject GetProject()
        {
            return projectModelElementEnvoy.GetValidProjectElement() as IProject;
        }

        public string GetPresentation()
        {
            // TODO: return methodName, or Class.MethodName if Class != TypeName
            return methodName;
        }

        public UnitTestNamespace GetNamespace()
        {
            return new UnitTestNamespace(typeName.GetNamespaceName());
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

            return (from member in declaredType.EnumerateMembers(methodName, declaredType.CaseSensistiveName)
                    let method = member as IMethod
                    where method != null && !method.IsAbstract && method.TypeParameters.Count <= 0 && (method.AccessibilityDomain.DomainType == AccessibilityDomain.AccessibilityDomainType.PUBLIC || method.AccessibilityDomain.DomainType == AccessibilityDomain.AccessibilityDomainType.INTERNAL)
                    select member).FirstOrDefault();
        }

        private ITypeElement GetDeclaredType()
        {
            var p = GetProject();
            if (p == null)
                return null;

            var psiModule = psiModuleManager.GetPrimaryPsiModule(p);
            if (psiModule == null)
                return null;

            return cacheManager.GetDeclarationsCache(psiModule, true, true).GetTypeElementByCLRName(typeName);
        }

        public IEnumerable<IProjectFile> GetProjectFiles()
        {
            var declaredType = GetDeclaredType();
            if (declaredType != null)
            {
                var result = (from sourceFile in declaredType.GetSourceFiles() 
                              select sourceFile.ToProjectFile()).ToList<IProjectFile>();
                if (result.Count == 1)
                    return result;
            }

            var declaredElement = GetDeclaredElement();
            if (declaredElement == null)
                return EmptyArray<IProjectFile>.Instance;

            return from sourceFile in declaredElement.GetSourceFiles()
                   select sourceFile.ToProjectFile();
        }

        // ReSharper 6.1
        public IList<UnitTestTask> GetTaskSequence(IList<IUnitTestElement> explicitElements)
        {
            return new List<UnitTestTask>
                       {
                           new UnitTestTask(null, new XunitTestAssemblyTask(TestClass.AssemblyLocation)),
                           new UnitTestTask(TestClass, new XunitTestClassTask(TestClass.AssemblyLocation, typeName.FullName, explicitElements.Contains(TestClass))),
                           new UnitTestTask(this, new XunitTestMethodTask(TestClass.AssemblyLocation, typeName.FullName, ShortName, explicitElements.Contains(this)))
                       };
        }

        // ReSharper 6.0
        public IList<UnitTestTask> GetTaskSequence(IEnumerable<IUnitTestElement> explicitElements)
        {
            return GetTaskSequence(explicitElements.ToList());
        }

        private XunitTestClassElement TestClass
        {
            get { return Parent as XunitTestClassElement; }
        }

        public string Kind
        {
            get { return "xUnit.net Test"; }
        }

        public IEnumerable<UnitTestElementCategory> Categories
        {
            get { return UnitTestElementCategory.Uncategorized; }
        }

        public string ExplicitReason { get; private set; }
        public string Id { get; private set; }
        public IUnitTestProvider Provider { get; private set; }

        public IUnitTestElement Parent
        {
            get { return parent; }
            set
            {
                if (parent == value)
                    return;

                if (parent != null)
                    parent.RemoveChild(this);
                parent = (XunitTestClassElement) value;
                if (parent != null)
                    parent.AddChild(this);
            }
        }

        public ICollection<IUnitTestElement> Children
        {
            get { return EmptyArray<IUnitTestElement>.Instance; }
        }

        public string ShortName
        {
            get { return methodName; }
        }

        public bool Explicit
        {
            get { return !string.IsNullOrEmpty(ExplicitReason); }
        }

        public UnitTestElementState State { get; set; }

        public bool Equals(IUnitTestElement other)
        {
            return Equals(other as XunitTestMethodElement);
        }

        public bool Equals(XunitTestMethodElement other)
        {
            if (other == null)
                return false;

            return Equals(typeName.FullName, other.typeName.FullName) &&
                   Equals(methodName, other.methodName);
        }

        public void WriteToXml(XmlElement element)
        {
            element.SetAttribute("projectId", GetProject().GetPersistentID());
            element.SetAttribute("typeName", typeName.FullName);
            element.SetAttribute("methodName", methodName);
            element.SetAttribute("skipReason", ExplicitReason);
        }

        internal static IUnitTestElement ReadFromXml(XmlElement parent, IUnitTestElement parentElement, ISolution solution, UnitTestElementFactory unitTestElementFactory)
        {
            var testClass = parentElement as XunitTestClassElement;
            if (testClass == null)
                throw new InvalidOperationException("parentElement should be xUnit.net test class");

            var typeName = parent.GetAttribute("typeName");
            var methodName = parent.GetAttribute("methodName");
            var projectId = parent.GetAttribute("projectId");
            var skipReason = parent.GetAttribute("skipReason");

            var project = (IProject)ProjectUtil.FindProjectElementByPersistentID(solution, projectId);
            if (project == null)
                return null;

            return unitTestElementFactory.GetOrCreateTestMethod(project, testClass, new ClrTypeName(typeName), methodName, skipReason);
        }
    }
}