# ABP `app.UseUnitOfWork()` reports failed writes as `200 OK`

With `app.UseUnitOfWork()` in the pipeline, as every ABP startup template has it, a request whose unit of work fails to complete still answers `200 OK` with the success body. The data is not saved.

This repository is a minimal ABP 10.7.0 host that shows it. It has one controller, one SQLite database and three pipeline variants.

## Why

`AbpUowActionFilter` joins the unit of work that `AbpUnitOfWorkMiddleware` reserved, so the filter only calls `SaveChangesAsync`. The transaction commit and the unit of work event handlers run in `uow.CompleteAsync()`, after `await next(context)` in the middleware. By then MVC has written the status line and the body. A failure there cannot change the status, so Kestrel aborts the chunked response.

Without the middleware, the action filter begins and completes its own unit of work. That happens before the result executes, so a failure becomes a normal `500`.

## The two failures

- `POST /api/orders` with `customerId: 2` inserts an order for a customer that does not exist. The foreign key is `DEFERRABLE INITIALLY DEFERRED`, so `SaveChanges` succeeds and the violation surfaces at `COMMIT`. It stands in for any commit-time failure: a deferred constraint, a serialization failure, a lost connection.
- `POST /api/orders/with-event` publishes a local event whose handler throws. Local event handlers run in `CompleteAsync`. A failing distributed event publish without an outbox behaves the same.

## Run the tests

```
dotnet test
```

The tests run each scenario against a real Kestrel server in three pipelines, selected by `Repro:Pipeline`:

| Pipeline | What it does | Failed commit | Failing handler |
| --- | --- | --- | --- |
| `template` | `app.UseUnitOfWork()`, as the startup templates do | `200 OK`, body cut off | `200 OK`, body cut off |
| `template-complete-on-response-starting` | the same, with `CompleteUnitOfWorkOnResponseStarting = true` (new in 10.7.0) | `500`, empty body | `500`, empty body |
| `without-uow-middleware` | no `app.UseUnitOfWork()` | `500`, ABP error JSON | `500`, ABP error JSON |

Seven tests pass. The two `template` tests fail, because the client receives `200 OK` and nothing was stored.

## Run it by hand

```
dotnet run --project src/Repro --urls http://localhost:5199 -- --Repro:Pipeline=template
```

In a second shell:

```
curl -i -X POST http://localhost:5199/api/orders -H "Content-Type: application/json" -d "{\"customerId\":2}"
curl -i -X POST http://localhost:5199/api/orders/with-event -H "Content-Type: application/json" -d "{\"customerId\":1}"
curl http://localhost:5199/api/orders/count
```

Output with the `template` pipeline:

```
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Transfer-Encoding: chunked

{"id":"443eb482-930f-a92c-a668-3a242ce61e4e"}curl: (18) transfer closed with outstanding read data remaining
```

The count stays `0`. The server log shows `An exception occurred, but response has already started!` and `SQLite Error 19: 'FOREIGN KEY constraint failed'` from `UnitOfWork.CompleteAsync` inside `AbpUnitOfWorkMiddleware`.

The client has the status line, the headers and the complete JSON body. Only the terminating chunk is missing. Whether a client notices depends on how strictly it reads the stream.
