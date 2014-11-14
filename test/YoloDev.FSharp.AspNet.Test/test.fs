module ``YoloDev FSharp for AspNet Test``

open Xunit

[<Fact>]
let ``it runs.`` () =
    Assert.True true

[<Fact>]
let ``it has access to dynamically compiled references.`` () =
    Assert.Equal (5L, YoloDev.FSharp.AspNet.Referenced.testNum)