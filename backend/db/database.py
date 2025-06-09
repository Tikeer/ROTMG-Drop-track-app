import os
import certifi
from pymongo import MongoClient
from dotenv import load_dotenv

load_dotenv()

uri = os.getenv("MONGODB_URI")

# Using certifi for proper SSL certificate verification
client = MongoClient(
    uri,
    tlsCAFile=certifi.where()
)

db = client["droptracker"]            # Nazwa Twojej bazy danych
drops = db["drops"]
characters = db["characters"] 
seasons = db["seasons"]