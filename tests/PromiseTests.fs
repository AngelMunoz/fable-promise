module PromiseTests

open System
open Fable.Core

type DisposableAction(f) =
    interface IDisposable with
        member __.Dispose() = f()

describe "Promise tests" <| fun _ ->
    it "Simple promise translates without exception" <| fun () ->
        promise { return () }

    it "Promise.map works" <| fun () ->
        Promise.lift "Hello"
        |> Promise.map (fun x ->
            x.Length |> equal 5
        )

    it "PromiseBuilder.Combine works" <| fun () ->
        let nums = [|1;2;3;4;5|]
        promise {
            let mutable xs = []
            for x in nums do
                let x = x + 1
                if x < 5 then
                    xs <- x::xs
            return xs
        }
        |> Promise.map (fun xs -> xs = [4;3;2] |> equal true)

    it "Promise for binding works correctly" <| fun () ->
        let inputs = [|1; 2; 3|]
        let result = ref 0
        promise {
            for inp in inputs do
                result := !result + inp
        }
        |> Promise.map (fun () -> equal !result 6)

    it "Promise while binding works correctly" <| fun () ->
        let mutable result = 0
        promise {
            while result < 10 do
                result <- result + 1
        }
        |> Promise.map (fun () -> equal result 10)

    it "Promise exceptions are handled correctly" <| fun () ->
        let result = ref 0
        let f shouldThrow =
            promise {
                try
                    if shouldThrow then failwith "boom!"
                    else result := 12
                with _ -> result := 10
            } |> Promise.map (fun () -> !result)
        promise {
            let! x = f true
            let! y = f false
            return x + y
        } |> Promise.map (equal 22)

    it "Simple promise is executed correctly" <| fun () ->
        let result = ref false
        let x = promise { return 99 }
        promise {
            let! x = x
            let y = 99
            result := x = y
        }
        |> Promise.map (fun () -> equal !result true)

    it "promise use statements should dispose of resources when they go out of scope" <| fun () ->
        let isDisposed = ref false
        let step1ok = ref false
        let step2ok = ref false
        let resource = promise {
            return new DisposableAction(fun () -> isDisposed := true)
        }
        promise {
            use! r = resource
            step1ok := not !isDisposed
        }
        |> Promise.map (fun () ->
            step2ok := !isDisposed
            (!step1ok && !step2ok) |> equal true)

    it "Try ... with ... expressions inside promise expressions work the same" <| fun () ->
        let result = ref ""
        let throw() : unit =
            raise(exn "Boo!")
        let append(x) =
            result := !result + x
        let innerPromise() =
            promise {
                append "b"
                try append "c"
                    throw()
                    append "1"
                with _ -> append "d"
                append "e"
            }
        promise {
            append "a"
            try do! innerPromise()
            with _ -> append "2"
            append "f"
        } |> Promise.map (fun () ->
            equal !result "abcdef")

    it "Promise try .. with returns correctly from 'with' branch" <| fun () ->
        let work = promise {
            try
              failwith "testing"
              return -1
            with e ->
              return 42 }
        work |> Promise.map (equal 42)

    // it "Deep recursion with promise doesn't cause stack overflow" <| fun () ->
    //     promise {
    //         let result = ref false
    //         let rec trampolineTest res i = promise {
    //             if i > 100000
    //             then res := true
    //             else return! trampolineTest res (i+1)
    //         }
    //         do! trampolineTest result 0
    //         equal !result true
    //     }

    it "Nested failure propagates in promise expressions" <| fun () ->
        promise {
            let data = ref ""
            let f1 x =
                promise {
                    try
                        failwith "1"
                        return x
                    with
                    | e -> return! failwith ("2 " + e.Message.Trim('"'))
                }
            let f2 x =
                promise {
                    try
                        return! f1 x
                    with
                    | e -> return! failwith ("3 " + e.Message.Trim('"'))
                }
            let f() =
                promise {
                    try
                        let! y = f2 4
                        return ()
                    with
                    | e -> data := e.Message.Trim('"')
                }
            do! f()
            do! Promise.sleep 100
            equal "3 2 1" !data
        }

    it "Try .. finally expressions inside promise expressions work" <| fun () ->
        promise {
            let data = ref ""
            do! promise {
                try data := !data + "1 "
                finally data := !data + "2 "
            }
            do! promise {
                try
                    try failwith "boom!"
                    finally data := !data + "3"
                with _ -> ()
            }
            do! Promise.sleep 100
            equal "1 2 3" !data
        }

    it "Final statement inside promise expressions can throw" <| fun () ->
        promise {
            let data = ref ""
            let f() = promise {
                try data := !data + "1 "
                finally failwith "boom!"
            }
            do! promise {
                try
                    do! f()
                    return ()
                with
                | e -> data := !data + e.Message.Trim('"')
            }
            do! Promise.sleep 100
            equal "1 boom!" !data
        }

    it "Promise.Bind propagates exceptions" <| fun () ->
        promise {
            let task2 name = promise {
                // printfn "testing with %s" name
                do! Promise.sleep 100 //difference between task1 and task2
                if name = "fail" then
                    failwith "Invalid access credentials"
                return "Ok"
            }

            let doWork name task =
                promise {
                    let! b =
                        task "fail"
                        |> Promise.catch (fun ex -> ex.Message)
                    return b
                }

            let! res2 = doWork "task2" task2
            equal "Invalid access credentials" res2
        }

    it "Promise.catchBind takes a Promise-returning function" <| fun () ->
        promise {
            let pr = promise {
                failwith "Boo!"
                return "Ok"
            }
            let exHandler (e: exn) = promise {
                return e.Message
            }

            let! res = pr |> Promise.catchBind exHandler
            res |> equal "Boo!"
        }

    it "Promise.either can take all combinations of value-returning and Promise-returning continuations" <| fun () ->
        promise {
            let failing = promise { failwith "Boo!" }
            let successful = Promise.lift 42

            let! r1 = successful |> Promise.either (fun x -> string x) (fun x -> failwith "Shouldn't get called")
            let! r2 = successful |> Promise.eitherBind (fun n -> string n |> Promise.lift) (fun x -> failwith "Shouldn't get called")

            let! r3 = failing |> Promise.either (fun x -> failwith "Shouldn't get called") (fun (ex:Exception) -> ex.Message)
            let! r4 = failing |> Promise.eitherBind (fun x -> failwith "Shouldn't get called") (fun (ex:Exception) -> Promise.lift ex.Message)

            r1 |> equal "42"
            r2 |> equal "42"
            r3 |> equal "Boo!"
            r4 |> equal "Boo!"
        }

    itSync "Promise.start works" <| fun () ->
        promise {
            // Whitespaces are just for a better display in the console
            printfn "    Promise started"
            return 5
        } |> Promise.start

    it "Promise mapResultError works correctly" <| fun () ->
        Result.Error "foo"
        |> Promise.lift
        |> Promise.mapResultError (fun (msg:string) -> 666)
        |> Promise.map (fun exnRes ->
            match exnRes with
            | Ok _ -> failwith "Shouldn't get called"
            | Error e -> equal 666 e
        )

    it "Promise.bindResult works" <| fun () ->
        let multiplyBy2 (value : int) =
            Promise.create (fun resolve reject ->
                resolve (value * 2)
            )

        Promise.lift 42
        |> Promise.result
        |> Promise.bindResult (fun value ->
            multiplyBy2 value
        )
        |> Promise.tap (fun result ->
            result |> equal (Ok (42 * 2))
        )

    it "Promise.tap passes original value through to next transform" <| fun () ->
        Promise.lift(5)
            |> Promise.tap(fun x ->
                x |> equal 5
                ()
            )
            |> Promise.map (fun x ->
                x |> equal 5
            )

    it "Promise.Parallel works" <| fun () ->
        let p1 =
            promise {
                do! Promise.sleep 100
                return 1
            }
        let p2 =
            promise {
                do! Promise.sleep 200
                return 2
            }
        let p3 =
            promise {
                do! Promise.sleep 300
                return 3
            }

        Promise.Parallel [p1; p2; p3]
        |> Promise.map (fun res ->
            res |> equal [|1; 2; 3 |]
        )


    it "Promise.all works" <| fun () ->
        let p1 =
            promise {
                do! Promise.sleep 100
                return 1
            }
        let p2 =
            promise {
                do! Promise.sleep 200
                return 2
            }
        let p3 =
            promise {
                do! Promise.sleep 300
                return 3
            }

        Promise.all [p1; p2; p3]
        |> Promise.map (fun res ->
            res |> equal [|1; 2; 3 |]
        )

    it "Promise.result maps to Result.Ok in case of success" <| fun () ->
        Promise.lift 42
        |> Promise.result
        |> Promise.tap (fun result ->
            result |> equal (Ok 42)
        )


    it "Promise.result maps to Result.Error in case of error" <| fun () ->
        Promise.reject (exn "Invalid value")
        |> Promise.result
        |> Promise.tap (fun result ->
            let msg =
                match result with
                | Ok _ -> ""
                | Error e -> e.Message
            msg |> equal "Invalid value"
        )

    it "Promise can be run in parallel with andFor extension" <| fun () ->
        let one = Promise.lift 1
        let two = Promise.lift 2
        promise {
            for a in one do
            andFor b in two
            return a + b
        }
        |> Promise.tap (fun result ->
            result |> equal 3
        )

    it "Promise can be run in parallel with and!" <| fun () ->
        let one = Promise.lift 1
        let two = Promise.lift 2
        promise {
            let! a = one
            and! b = two
            return a + b
        }
        |> Promise.tap (fun result ->
            result |> equal 3
            ()
        )

    it "Promise can run multiple tasks in parallel with and!" <| fun () ->
        let mutable s = ""
        let doWork ms letter = promise {
            do! Promise.sleep ms
            s <- s + letter
            return letter
        }
        let one = doWork 1000 "a"
        let two = doWork 500 "b"
        let three = doWork 200 "c"
        promise {
            let! a = one
            and! b = two
            and! c = three
            return a + b + c
        }
        |> Promise.tap (fun result ->
            result |> equal "abc"
            s |> equal "cba"
        )

    it "Promise can run multiple tasks in parallel with andFor extension" <| fun () ->
        let one = Promise.lift 1
        let two = Promise.lift 2
        let three = Promise.lift 3
        promise {
            for a in one do
            andFor b in two
            andFor c in three
            return a + b = c
        }
        |> Promise.tap (fun result ->
            result |> equal true
            ()
        )

    it "Promise does not re-execute multiple times" <| fun () ->
        let mutable promiseExecutionCount = 0
        let p = promise {
            promiseExecutionCount <- promiseExecutionCount + 1
            return 1
        }

        p.``then``(fun _ -> promiseExecutionCount |> equal 1) |> ignore
        p.``then``(fun _ -> promiseExecutionCount |> equal 1) |> ignore
        p.``then``(fun _ -> promiseExecutionCount |> equal 1)

    it "Promise is hot" <| fun () ->
        let mutable promiseExecutionCount = 0

        // start a promise
        let _ = promise {
            promiseExecutionCount <- promiseExecutionCount + 1
            return 1
        }

        // wait a bit, then check that the first promise was executed.
        let delayed = Promise.create(fun ok er ->
            JS.setTimeout (fun () -> ok()) 10 (* ms *) |> ignore
        )

        delayed.``then``(fun _ -> promiseExecutionCount |> equal 1)
