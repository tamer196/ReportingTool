FROM alpine:3.24
RUN apk add --no-cache poppler-utils qpdf font-noto font-noto-arabic \
    && adduser -D -u 10001 evidence
USER evidence
