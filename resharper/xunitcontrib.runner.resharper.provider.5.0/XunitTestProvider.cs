using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application;
using JetBrains.CommonControls;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.TaskRunnerFramework;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.UI;
using JetBrains.TreeModels;
using JetBrains.UI.TreeView;
using JetBrains.Util;
using Xunit.Sdk;
using XunitContrib.Runner.ReSharper.RemoteRunner;
using XunitContrib.Runner.ReSharper.UnitTestProvider.Properties;

namespace XunitContrib.Runner.ReSharper.UnitTestProvider
{
    [UnitTestProvider, UsedImplicitly]
    public class XunitTestProvider : IUnitTestProvider
    {
        private static readonly XunitBrowserPresenter Presenter = new XunitBrowserPresenter();
        private static readonly AssemblyLoader AssemblyLoader = new AssemblyLoader();
        private static readonly CLRTypeName PropertyDataAttributeName = new CLRTypeName("Xunit.Extensions.PropertyDataAttribute");

        static XunitTestProvider()
        {
            // ReSharper automatically adds all test providers to the list of assemblies it uses
            // to handle the AppDomain.Resolve event

            // The test runner process talks to devenv/resharper via remoting, and so devenv needs
            // to be able to resolve the remote assembly to be able to recreate the serialised types.
            // (Aside: the assembly is already loaded - why does it need to resolve it again?)
            // ReSharper automatically adds all unit test provider assemblies to the list of assemblies
            // it uses to handle the AppDomain.Resolve event. Since we've got a second assembly to
            // live in the remote process, we need to add this to the list.
            AssemblyLoader.RegisterAssembly(typeof(XunitTaskRunner).Assembly);
        }

        public string ID
        {
            get { return XunitTaskRunner.RunnerId; }
        }

        public string Name
        {
            get { return "xUnit.net"; }
        }

        public Image Icon
        {
            get { return Resources.xunit; }
        }

        public int CompareUnitTestElements(UnitTestElement x, UnitTestElement y)
        {
            if (Equals(x, y))
                return 0;

            int compare = StringComparer.CurrentCultureIgnoreCase.Compare(x.GetTypeClrName(), y.GetTypeClrName());
            if (compare != 0)
                return compare;

            if (x is XunitTestElementMethod && y is XunitTestElementClass)
                return -1;

            if (x is XunitTestElementClass && y is XunitTestElementMethod)
                return 1;

            if (x is XunitTestElementClass && y is XunitTestElementClass)
                return 0;

            var xe = (XunitTestElementMethod)x;
            var ye = (XunitTestElementMethod)y;
            return xe.Order.CompareTo(ye.Order);
        }

        public UnitTestElement Deserialize(ISolution solution, string elementString)
        {
            return null;
        }

        // Provides Reflection-like metadata of a physical assembly, called at startup (if the
        // assembly exists) and whenever the assembly is recompiled. It allows us to retrieve
        // the tests that will actually get executed, as opposed to ExploreFile, which is about
        // identifying tests as they are being written, and finding their location in the source
        // code.
        // It would be nice to check to see that the assembly references xunit before iterating
        // through all the types in the assembly - a little optimisation. Unfortunately,
        // when an assembly is compiled, only assemblies that have types that are directly
        // referenced are embedded as references. In other words, if I use something from
        // xunit.extensions, but not from xunit (say I only use a DerivedFactAttribute),
        // then only xunit.extensions is listed as a referenced assembly. xunit will still
        // get loaded at runtime, because it's a referenced assembly of xunit.extensions.
        // It's also needed at compile time, but it's not a direct reference.
        // So I'd need to recurse into the referenced assemblies references, and I don't
        // quite know how to do that, and it's suddenly making our little optimisation
        // rather complicated. So (at least for now) we'll leave well enough alone and
        // just explore all the types
        public void ExploreAssembly(IMetadataAssembly assembly,
                                    IProject project,
                                    UnitTestElementConsumer consumer)
        {
            assembly.ProcessExportedTypes(new XunitAssemblyExplorer(this, assembly, project, consumer));

        }

        public ProviderCustomOptionsControl GetCustomOptionsControl(ISolution solution)
        {
            return null;
        }

        public void ExploreExternal(UnitTestElementConsumer consumer)
        {
            // Called from a refresh of the Unit Test Explorer
            // Allows us to explore anything that's not a part of the solution + projects world
        }

        public void ExploreFile(IFile psiFile,
                                UnitTestElementLocationConsumer consumer,
                                CheckForInterrupt interrupted)
        {
            if (psiFile == null)
                throw new ArgumentNullException("psiFile");

            psiFile.ProcessDescendants(new XunitFileExplorer(this, consumer, psiFile, interrupted));
        }

        public void ExploreSolution(ISolution solution, UnitTestElementConsumer consumer)
        {
            // Called from a refresh of the Unit Test Explorer
            // Allows us to explore the solution, without going into the projects
        }


        public RemoteTaskRunnerInfo GetTaskRunnerInfo()
        {
            return new RemoteTaskRunnerInfo(typeof(XunitTaskRunner));
        }

        // This method gets called to generate the tasks that the remote runner will execute
        // When we run all the tests in a class (by e.g. clicking the menu in the margin marker)
        // this method is called with a class element and the list of explicit elements contains
        // one item - the class. We should add all tasks required to prepare to run this class
        // (e.g. loading the assembly and loading the class via reflection - but NOT running the
        // test methods)
        // It is then subsequently called with all method elements, and with the same list (that
        // only has the class as an explicit element). We should return a new sequence of tasks
        // that would allow running the test methods. This will probably be a superset of the class
        // sequence of tasks (e.g. load the assembly, load the class via reflection and a task
        // to run the given method)
        // Because the tasks implement IEquatable, they can be compared to collapse the multiple
        // lists of tasks into a tree (or set of trees) with only one instance of each unique
        // task in order, so in the example, we load the assmbly once, then we load the class once,
        // and then we have multiple tasks for each running test method. If there are multiple classes
        // run, then multiple class lists will be created and collapsed under a single assembly node.
        // If multiple assemblies are run, then there will be multiple top level assembly nodes, each
        // with one or more uniqe class nodes, each with one or more unique test method nodes
        // If you explicitly run a test method from its menu, then it will appear in the list of
        // explicit elements.
        // ReSharper provides an AssemblyLoadTask that can be used to load the assembly via reflection.
        // In the remote runner process, this task is serviced by a runner that will create an app domain
        // shadow copy the files (which we can affect via ProfferConfiguration) and then cause the rest
        // of the nodes (e.g. the classes + methods) to get executed (by serializing the nodes, containing
        // the remote tasks from these lists, over into the new app domain). This is the default way the
        // nunit and mstest providers work.
        public IList<UnitTestTask> GetTaskSequence(UnitTestElement element, IList<UnitTestElement> explicitElements)
        {
            var testMethod = element as XunitTestElementMethod;
            if (testMethod != null)
            {
                XunitTestElementClass testClass = testMethod.Class;
                var result = new List<UnitTestTask>
                                 {
                                     new UnitTestTask(null, CreateAssemblyTask(testClass.AssemblyLocation)),
                                     new UnitTestTask(testClass, CreateClassTask(testClass, explicitElements)),
                                     new UnitTestTask(testMethod, CreateMethodTask(testMethod, explicitElements))
                                 };

                return result;
            }

            // We don't have to do anything explicit for a test class, because when a class is run
            // we get called for each method, and each method already adds everything we need (loading
            // the assembly and the class)
            if (element is XunitTestElementClass)
                return EmptyArray<UnitTestTask>.Instance;

            throw new ArgumentException(string.Format("element is not xUnit: '{0}'", element));
        }

        private static RemoteTask CreateAssemblyTask(string assemblyLocation)
        {
            return new XunitTestAssemblyTask(assemblyLocation);
        }

        private static RemoteTask CreateClassTask(XunitTestElementClass testClass, ICollection<UnitTestElement> explicitElements)
        {
            return new XunitTestClassTask(testClass.AssemblyLocation, testClass.GetTypeClrName(), explicitElements.Contains(testClass));
        }

        private static RemoteTask CreateMethodTask(XunitTestElementMethod testMethod, ICollection<UnitTestElement> explicitElements)
        {
            return new XunitTestMethodTask(testMethod.Class.AssemblyLocation, testMethod.Class.GetTypeClrName(), testMethod.MethodName, explicitElements.Contains(testMethod));
        }

        // Used to discover the type of the element - unknown, test, test container (class) or
        // something else relating to a test element (e.g. parent class of a nested test class)
        // This method is called to get the icon for the completion lists, amongst other things
        public bool IsElementOfKind(IDeclaredElement declaredElement, UnitTestElementKind elementKind)
        {
            switch (elementKind)
            {
                case UnitTestElementKind.Unknown:
                    return !IsUnitTestElement(declaredElement);

                case UnitTestElementKind.Test:
                    return IsUnitTest(declaredElement);

                case UnitTestElementKind.TestContainer:
                    return IsUnitTestContainer(declaredElement);

                case UnitTestElementKind.TestStuff:
                    return IsUnitTestElement(declaredElement);
            }

            return false;
        }

        public static bool IsUnitTestElement(IDeclaredElement element)
        {
            return IsUnitTestMethod(element) || IsUnitTestProperty(element) || IsUnitTestClass(element);
        }

        private static bool IsUnitTestMethod(IDeclaredElement element)
        {
            return IsUnitTest(element);
        }

        private static bool IsUnitTestProperty(IDeclaredElement element)
        {
            return element is IProperty && IsTheoryPropertyDataProperty((IProperty) element);
        }

        private static bool IsUnitTestClass(IDeclaredElement element)
        {
            return element is IClass && (IsUnitTestContainer(element) || ContainsUnitTestElement((IClass) element));
        }

        private static bool IsTheoryPropertyDataProperty(ITypeMember element)
        {
            if (element.IsStatic && element.GetAccessRights() == AccessRights.PUBLIC)
            {
                // According to msdn, parameters to the constructor are positional parameters, and any
                // public read-write fields are named parameters. The name of the property we're after
                // is not a public field/property, so it's a positional parameter
                var propertyNames = from method in element.GetContainingType().Methods
                                    from attributeInstance in method.GetAttributeInstances(PropertyDataAttributeName, false)
                                    select attributeInstance.PositionParameter(0).ConstantValue.Value as string;
                return propertyNames.Any(name => name == element.ShortName);
            }

            return false;
        }

        // Returns true if the given element contains an element that is either a
        // unit test or (more likely) a unit test container (class)
        // (i.e. a nested a class that contains a test class)
        // See the comment to SuppressUnusedXunitTestElements for more info
        private static bool ContainsUnitTestElement(ITypeElement element)
        {
            return element.NestedTypes.Aggregate(false, (current, nestedType) => IsUnitTestElement(nestedType) || current);
        }

        private static bool IsUnitTest(IDeclaredElement element)
        {
            var testMethod = element as IMethod;
            return testMethod != null && MethodUtility.IsTest(MethodWrapper.Wrap(testMethod));
        }

        private static bool IsUnitTestContainer(IDeclaredElement element)
        {
            var testClass = element as IClass;
            return testClass != null && TypeUtility.IsTestClass(TypeWrapper.Wrap(testClass));
        }

        public void Present(UnitTestElement element,
                            IPresentableItem presentableItem,
                            TreeModelNode node,
                            PresentationState state)
        {
            Presenter.UpdateItem(element, node, presentableItem, state);
        }

        public string Serialize(UnitTestElement element)
        {
            return null;
        }
    }
}