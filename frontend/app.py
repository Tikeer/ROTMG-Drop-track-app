from backend.logic import AppLogic
import customtkinter as ctk

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

class RotmgApp(ctk.CTk):
    def __init__(self):
        super().__init__()
        self.title("RotMG Drop Tracker")
        self.geometry("1200x800")
        self.logic = AppLogic(self)
        self.setup_ui()

    
    def setup_ui(self):
        label = ctk.CTkLabel(self, text="Welcome to RotMG Drop Tracker!")
        label.pack(pady=20)

        new_char_butt = ctk.CTkButton(self,text="Create new character",command=self.logic.new_char)
        new_char_butt.pack(pady=10)