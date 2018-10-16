open Expecto

[<EntryPoint>]
let main argv =
    let config = 
        match Seq.tryHead argv with
        | Some "DEBUG" -> { defaultConfig with ``parallel`` = false;}
        | _ -> defaultConfig
    Tests.runTestsInAssembly config argv
