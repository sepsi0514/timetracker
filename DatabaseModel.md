# Database model

The SQLite database stores work items and their sessions. An item's displayed total is the sum of its sessions' `DurationMinutes`; it is not stored separately. Only one session may be active at a time (`EndedAt` is `NULL`).

```plantuml
@startuml
hide methods
hide stereotypes

entity "WorkItems" as WorkItems {
  * Id : INTEGER <<PK>>
  --
  * Name : TEXT <<unique, case-insensitive>>
}

entity "WorkSessions" as WorkSessions {
  * Id : INTEGER <<PK>>
  --
  * WorkItemId : INTEGER <<FK>>
  * StartedAt : TEXT
    EndedAt : TEXT <<nullable>>
  * DurationMinutes : INTEGER <<default 0>>
}

WorkItems ||--o{ WorkSessions : has
note right of WorkSessions
  At most one row can have
  EndedAt = NULL (active session).
end note
@enduml
```
