Imports System
Imports System.Collections.Generic
Imports Xunit
Imports Xunit.Extensions

Public Class MyTest
    Inherits PropertyDataBase

    <Theory>
    <PropertyData("DataEnumerator")>
    Public Sub Test1(ByVal value As Int32)
        Assert.Equal(42, value)
    End Sub
End Class

Public Class PropertyDataBase
    Public Shared ReadOnly Iterator Property DataEnumerator() As IEnumerable(Of Object())
        Get
            Yield New Object() { 42 }
        End
    End Property
End Class
