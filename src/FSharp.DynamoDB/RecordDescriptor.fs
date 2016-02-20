﻿namespace FSharp.DynamoDB

open System
open System.Collections.Generic
open System.Collections.Concurrent

open Microsoft.FSharp.Quotations

open Amazon.DynamoDBv2
open Amazon.DynamoDBv2.Model

open FSharp.DynamoDB.FieldConverter
open FSharp.DynamoDB.RecordSchema
open FSharp.DynamoDB.ConditionalExpr
open FSharp.DynamoDB.UpdateExpr

type internal RecordDescriptor<'Record> internal () =
    let converter = FieldConverter.resolve<'Record>() :?> RecordConverter<'Record>
    let keyStructure = KeyStructure.FromRecordInfo converter.RecordInfo
    let keySchema = TableKeySchema.OfKeyStructure keyStructure
    let exprCmp = new ExprEqualityComparer()
    let conditionals = new ConcurrentDictionary<Expr, ConditionalExpression>(exprCmp)
    let updaters = new ConcurrentDictionary<Expr, UpdateExpression>(exprCmp)

    member __.KeySchema = keySchema
    member __.Info = converter.RecordInfo
    member __.ToAttributeValues(key : TableKey) = KeyStructure.ExtractKey(keyStructure, key)

    member __.ExtractKey(record : 'Record) = 
        KeyStructure.ExtractKey(keyStructure, converter.RecordInfo, record)

    member __.ExtractConditional(expr : Expr<'Record -> bool>) : ConditionalExpression =
        conditionals.GetOrAdd(expr, fun _ -> extractQueryExpr converter.RecordInfo expr)

    member __.ExtractUpdater(expr : Expr<'Record -> 'Record>) : UpdateExpression =
        updaters.GetOrAdd(expr, fun _ -> extractUpdateExpr converter.RecordInfo expr)

    member __.ToAttributeValues(record : 'Record) =
        let kv = converter.OfRecord record

        match keyStructure with
        | DefaultHashKey(name, hashKey, converter, _) ->
            let av = hashKey |> converter.OfFieldUntyped |> Option.get
            kv.Add(name, av)
        | _ -> ()

        kv

    member __.OfAttributeValues(ro : RestObject) = converter.ToRecord ro

type internal RecordDescriptor private () =
    static let descriptors = new ConcurrentDictionary<Type, Lazy<obj>>()
    static member Create<'TRecord> () =
        let rd = lazy(new RecordDescriptor<'TRecord>() :> obj)
        descriptors.GetOrAdd(typeof<'TRecord>, rd).Value :?> RecordDescriptor<'TRecord>