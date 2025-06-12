import customtkinter as ctk
from PIL import Image 
import os

class AppLogic:
    def __init__(self,master_app_instance):
        self.master_app_instance = master_app_instance
        self.current_character_choice = None
        self.new_char_toplevel = None
        self.sprite_display = SpriteDisplay(master_app_instance)

    def option(self,choice,window):
        self.sprite_display.optionmenu_callback(choice,window)
        
    #toplevel - wyskakujace okienko dziecko glownego okna
    def new_char(self):
        if self.new_char_toplevel is None or not self.new_char_toplevel.winfo_exists():
            self.new_char_toplevel = ctk.CTkToplevel(self.master_app_instance)
            self.new_char_toplevel.title("Create new character")
            self.new_char_toplevel.geometry("400x300")
            self.new_char_toplevel.grab_set()


            optionmenu = ctk.CTkOptionMenu(self.new_char_toplevel, values=[
                "Rogue","Archer","Wizard","Priest",
                "Warrior","Knight","Paladin","Assassin",
                "Necromancer","Huntress","Mystic","Trickster",
                "Sorcerer","Ninja","Samurai","Bard",
                "Summoner","Kensei"
            ],command=lambda choice: self.option(choice,self.new_char_toplevel))
            optionmenu.pack(pady=10)

            char_name = ctk.CTkEntry(self.new_char_toplevel,placeholder_text="Char name")
            char_name.pack(pady=10)


class SpriteDisplay:
    def __init__(self,master_app_instance):
        self.master_app_instance = master_app_instance
        self.new_char_toplevel = None
        self.label = None
        
    def optionmenu_callback(self,choice,window):
        base_dir = os.path.dirname(__file__)
        self.new_char_choice = choice
        self.assets_path = os.path.join(base_dir,"..","assets","icons",choice + ".png")
        sprite = Image.open(self.assets_path)
        self.sprite = ctk.CTkImage(sprite,size=(70,70))
        

        if self.label is None:
            self.label = ctk.CTkLabel(window, image=self.sprite,text="")
            self.label.pack(pady=20,)
        else:
            self.label.configure(image=self.sprite)

        "def add_new_char_db():"
