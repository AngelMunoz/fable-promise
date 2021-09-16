module PromiseLikeTests

open Fable.Core
open Promise.Extension

/// this is the definition of a thenable from ts2fable's generation VsCode API
type [<AllowNullLiteral>] Thenable<'T> =
    abstract ``then``: ?onfulfilled: ('T -> U2<'TResult, Thenable<'TResult>>) * ?onrejected: (obj option -> U2<'TResult, Thenable<'TResult>>) -> Thenable<'TResult>
    abstract ``then``: ?onfulfilled: ('T -> U2<'TResult, Thenable<'TResult>>) * ?onrejected: (obj option -> unit) -> Thenable<'TResult>

module Thenable =
    let toPromise (t: Thenable<'t>): JS.Promise<'t> =  unbox t
    let ofPromise (p: JS.Promise<'t>): Thenable<'t> = unbox p

type Promise.PromiseBuilder with
    /// to make a value interop with the promise builder, you have to add an
    /// overload of the `Source` member to convert from your type to a promise.
    /// because thenables are trivially convertible, we can just unbox them.
    member x.Source(t: Thenable<'t>): JS.Promise<'t> = Thenable.toPromise t


describe "Promise like tests" <| fun _ ->

    it "Promise can interop with thenables" <| fun () ->
        let sampleThenable () =
            promise {
                return 1
            }
            |> Thenable.ofPromise

        promise {
            let! initialValue = sampleThenable()
            initialValue |> equal 1
        }
