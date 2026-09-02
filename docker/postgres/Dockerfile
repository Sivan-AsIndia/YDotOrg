# =================================================================================================
# The platform's PostgreSQL image.
#
# WHY AN IMAGE OF OUR OWN rather than postgres:17-alpine directly. The compose file publishes it as
# sivan67906/ydot-postgres:9.0.0 so the whole platform ships as one set of versioned images that
# move together - pulling ydot-iam:9.0.0 and getting whatever postgres:17-alpine happens to be
# today is how a database server changes underneath a running system.
#
# It is otherwise the stock image: no extensions, no seed data. The four services own their own
# schema and migrate it themselves on startup.
# =================================================================================================
FROM postgres:17-alpine

# The database the four services share. Each owns its own table prefix inside it.
ENV POSTGRES_DB=ydotphaseupdated

# UTC everywhere. Every timestamp the platform stores is UTC, and a server in another zone would
# make `now()` in a manual query disagree with what the application wrote.
ENV TZ=UTC
ENV PGTZ=UTC

EXPOSE 5432
