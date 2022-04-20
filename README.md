# html-cache-sharp

HTML cache (prerender) app for Bolt System projects

## Usage

- sample usage:

```bash
html-cache-sharp
```

- verbose output:

```bash
html-cache-sharp -v
html-cache-sharp --verbose
```

- with config:

```bash
html-cache-sharp --config-path=/path/to/config.yml
html-cache-sharp -c /path/to/config.yml
```

- with chrome revision:

```bash
html-cache-sharp --chrome-revision=972766
html-cache-sharp -r 972766
```

## Config examples

- with MongoDB:

```yml
db_driver: mongodb
db:
    host: localhost
    port: 27017
    user: ""
    passwd: ""
    db: project_db
    collection: renders
base_uri: https://example.com
page_types:
  - type: home
    regex: \.(ru|com)\/?$
```

- with Redis:

```yml
db_driver: redis
db:
    host: localhost
    port: 6379
    user: ""
    passwd: ""
    db: 4
base_uri: https://example.com
page_types:
  - type: home
    regex: \.(ru|com)\/?$
```

- with Redis:

```yml
db_driver: redis
db:
    host: localhost
    port: 6379
    user: ""
    passwd: ""
    db: db_name
    table: renders
base_uri: https://example.com
page_types:
  - type: home
    regex: \.(ru|com)\/?$
```
