from pymongo import MongoClient
import certifi
import customtkinter as ctk
import os
from dotenv import load_dotenv

load_dotenv()

uri = os.getenv("MONGODB_URI")

# Using certifi for proper SSL certificate verification
client = MongoClient(
    uri,
    tlsCAFile=certifi.where()
)

db = client["droptracker"]            # Nazwa Twojej bazy danych
drops = db["drops"]                   # Kolekcja


ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

app = ctk.CTk()
app.geometry("1200x800")
app.title("RotMG Drop Tracker")

label = ctk.CTkLabel(app,text="Welcome in RotMG Drop Tracker!")
label.pack(pady=20)

def klik():
    print("I've been clicked!!")

button = ctk.CTkButton(app,text="Click me!", command=klik)
button.pack(pady=10)

entry = ctk.CTkEntry(app,placeholder_text="Type smth here")
entry.pack(pady=10)


app.mainloop()