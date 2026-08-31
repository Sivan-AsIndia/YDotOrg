import { Component, inject, OnInit, OnDestroy, ElementRef } from '@angular/core';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationStart, Router } from '@angular/router';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-rainbow-loader',
  imports: [],
  templateUrl: './rainbow-loader.html',
  styleUrl: './rainbow-loader.css',
})
export class RainbowLoaderComponent implements OnInit, OnDestroy {
  private router = inject(Router);
  private el = inject(ElementRef);
  private sub!: Subscription;

  ngOnInit(): void {
    // Show loader immediately
    this.el.nativeElement.classList.add('active');

    // Hide after app bootstrap
    setTimeout(() => {
      this.el.nativeElement.classList.remove('active');
    }, 2000);

    // Route change listener
    this.sub = this.router.events.subscribe((event: any) => {
      if (event instanceof NavigationStart) {
        this.el.nativeElement.classList.add('active');
      }
      if (event instanceof NavigationEnd || 
          event instanceof NavigationCancel || 
          event instanceof NavigationError) {
        setTimeout(() => {
          this.el.nativeElement.classList.remove('active');
        }, 800);
      }
    });
  }

  ngOnDestroy(): void {
    if (this.sub) {
      this.sub.unsubscribe();
    }
  }
}