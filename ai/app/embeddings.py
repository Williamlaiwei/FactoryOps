from functools import lru_cache
import os

from fastembed import TextEmbedding

MODEL = "BAAI/bge-small-en-v1.5"


@lru_cache(maxsize=1)
def model():
    return TextEmbedding(model_name=MODEL, cache_dir=os.environ.get("FASTEMBED_CACHE_PATH", "/opt/models"), threads=2)


def passages(texts):
    return [vector.tolist() for vector in model().passage_embed(texts, batch_size=32)]


def query(text):
    return next(model().query_embed(text)).tolist()
