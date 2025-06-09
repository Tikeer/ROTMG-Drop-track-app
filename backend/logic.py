import customtkinter as ctk


class AppLogic:
    def __init__(self,master_app_instance):
        self.master_app_instance = master_app_instance
        self.current_character_choice = None
        self.new_char_toplevel = None

    def optionmenu_callback(self,choice):
        self.current_character_choice = choice
        
    #toplevel - wyskakujace okienko dziecko glownego okna
    def new_char(self):
        if self.new_char_toplevel is None or not self.new_char_toplevel.winfo_exists():
            self.new_char_toplevel = ctk.CTkToplevel(self.master_app_instance)
            self.new_char_toplevel.title("Create new character")
            self.new_char_toplevel.geometry("400x300")
            self.new_char_toplevel.grab_set()


            optionmenu = ctk.CTkOptionMenu(self.new_char_toplevel, values=[
                "Rogue","Archer","Wizard","Priest",
                "Warrior","Knight","Paladin","Assasin",
                "Necromancer","Huntress","Mystic","Trickster",
                "Sorcerer","Ninja","Samurai","Bard",
                "Summoner","Kensei"
            ],command=self.optionmenu_callback)
            optionmenu.pack(pady=10)

    "def add_new_char_db():"
