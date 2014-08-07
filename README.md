FSharpSupport
=============
![build status](http://img.shields.io/appveyor/ci/Alxandr/fsharpsupport-117.svg?style=flat)
![repo size](https://reposs.herokuapp.com/?path=YoloDev/FSharpSupport&style=flat)

FSharp Support for K

Usage
===
#### Step 1.
Add the yolodev myget feed to your `NuGet.Config` file. Sample:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="YoloDev" value="https://www.myget.org/F/yolodev/api/v2" />
    <add key="AspNetVNext" value="https://www.myget.org/F/aspnetvnext/api/v2" />
    <add key="NuGet.org" value="https://nuget.org/api/v2/" />
  </packageSources>
</configuration>
```

#### Step 2.
Create a new project, add the current project as a dependency, and add a "language" key to the project.json. You also need to add sources, since the default one just goes looking for *.cs. This is also quite important in F#, since file order matters. Sample:

```js
{
    "dependencies": {
        "FSharpSupport": "0.1-*"
    },
    "code": [ "file1.fs", "file2.fs" ],
    "language": {
        "name": "F#",
        "assembly": "FSharpSupport",
        "projectReferenceProviderType": "FSharpSupport.FSharpProjectReferenceProvider"
    },
    "frameworks": {
        "net45": { }
    }
}
```

#### Step 3.
Code in F#.

#### Step 4.
????

#### Step 5.
Profit.
