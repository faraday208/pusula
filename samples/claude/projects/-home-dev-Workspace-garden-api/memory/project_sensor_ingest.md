---
name: project-sensor-ingest
description: Sensor readings arrive in batches of 100 over MQTT
type: project
---
The garden-api service receives soil moisture and temperature readings in batches of 100 over MQTT, and stores them per greenhouse.

**Why:** the sensors sleep between reports to save battery, so they send in bursts instead of one reading at a time.

**How to apply:** make the ingest endpoint idempotent per batch id, and never assume readings arrive in time order.
