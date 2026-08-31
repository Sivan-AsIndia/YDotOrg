import { Component, OnInit } from '@angular/core';
import { FooterComponent } from '../footer/footer';
import { SidebarComponent } from '../sidebar/sidebar';
import { ThemeComponent } from '../theme/theme';
import { TopheaderComponent } from '../topheader/topheader';
import { LayoutService } from '../../Service/layout-service';
import { RouterOutlet } from '@angular/router';
import { RainbowLoaderComponent } from '../components/rainbow-loader/rainbow-loader';

@Component({
  selector: 'app-applayout',
  imports: [RouterOutlet,SidebarComponent,TopheaderComponent,FooterComponent,ThemeComponent,RainbowLoaderComponent],
  templateUrl: './applayout.html',
  styleUrl: './applayout.css',
})
export class ApplayoutComponent implements OnInit {
  constructor(private layoutService: LayoutService) {}

  ngOnInit(): void {
    this.layoutService.init();
    
  }


}
